using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;

/// <summary>
///     AB#6671: picks <b>one</b> version per dependency model name for a range-retaining resolve.
///     <para>
///         The classic catalog walk resolves every queued range on its own to the highest catalog version. In a
///         range-retaining resolve that is wrong when exact pins (classic dependencies, <c>System-[2.5.0]</c>) and
///         ranges (<c>System-[2.5,3.0)</c>) meet: they resolve to 2.5.0 and 2.6.0 although 2.5.0 satisfies both.
///         The solver collects every constraint (range or exact pin) of one name over the whole closure and chooses
///         the highest catalog version that satisfies all of them. The closure depends on the chosen versions (a
///         chosen model contributes its own constraints), so the choice is iterated to a fixed point.
///     </para>
///     <para>
///         No backtracking over versions of the dependents: each dependent contributes the constraints of the version
///         that was chosen for it (the highest one in range). When no version of a name satisfies all constraints the
///         solver fails and names every constraint, its origin and the visible versions.
///     </para>
/// </summary>
internal sealed class CatalogConstraintSolver(ICatalogManager catalogManager, ILogger logger)
{
    /// <summary>One constraint on a model name and the model that contributes it (null = the root).</summary>
    internal sealed record Constraint(CkModelIdVersionRange Range, CkModelId? Origin);

    /// <summary>The chosen version per model name. A name without an entry has no resolvable version.</summary>
    internal sealed class Solution(IReadOnlyDictionary<string, CkModelId> chosen)
    {
        public ModelExistingResult Lookup(CkModelIdVersionRange range) =>
            chosen.TryGetValue(range.Name, out var modelId) && range.IsSatisfiedBy(modelId)
                ? new ModelExistingResult { Exists = true, ModelId = modelId }
                : new ModelExistingResult { Exists = false };
    }

    public async Task<Solution> SolveAsync(IEnumerable<CkModelIdVersionRange> rootRanges, object? sourceIdentifier)
    {
        var roots = rootRanges.ToList();
        var existingCache = new Dictionary<string, ModelExistingResult>(StringComparer.Ordinal);
        var modelCache = new Dictionary<CkModelId, IReadOnlyCollection<CkModelIdVersionRange>?>();
        var chosen = new Dictionary<string, CkModelId>(StringComparer.Ordinal);

        // Each round discovers one more level of the closure, so the number of rounds is bounded by the depth of the
        // chain, not by a constant. The state space is finite (versions per name); a repeated selection that is not a
        // fixed point is an oscillation and fails instead of looping.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var constraints = await CollectAsync(roots, chosen, modelCache, sourceIdentifier).ConfigureAwait(false);

            var next = new Dictionary<string, CkModelId>(StringComparer.Ordinal);
            var unsatisfiable = new List<(string Name, List<Constraint> Constraints)>();
            foreach (var entry in constraints)
            {
                var name = entry.Key;
                var nameConstraints = entry.Value;
                var pick = await PickAsync(name, nameConstraints, existingCache, sourceIdentifier)
                    .ConfigureAwait(false);
                if (pick.ModelId != null)
                {
                    next[name] = pick.ModelId;
                }
                else if (pick.EveryRangeHasAVersion)
                {
                    unsatisfiable.Add((name, nameConstraints));
                }
                // A range without any catalog version stays unresolved; the caller reports it as unknown model.
            }

            if (next.Count == chosen.Count && next.All(kv => chosen.TryGetValue(kv.Key, out var c) && c == kv.Value))
            {
                if (unsatisfiable.Count > 0)
                {
                    var (name, nameConstraints) = unsatisfiable[0];
                    throw ModelValidationException.UnsatisfiableDependencyConstraints(name,
                        nameConstraints.Select(c => (c.Range, c.Origin)),
                        await DescribeVisibleVersionsAsync(name, sourceIdentifier).ConfigureAwait(false));
                }

                return new Solution(chosen);
            }

            var key = string.Join(";", next.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value.FullName));
            if (!seen.Add(key))
            {
                throw new ModelValidationException(
                    $"The dependency versions of the construction kit models {string.Join(", ", roots.Select(r => $"'{r}'"))} " +
                    "repeat without settling on one version per model. Narrow the dependency ranges.");
            }

            chosen = next;
        }
    }

    private async Task<Dictionary<string, List<Constraint>>> CollectAsync(List<CkModelIdVersionRange> roots,
        Dictionary<string, CkModelId> chosen,
        Dictionary<CkModelId, IReadOnlyCollection<CkModelIdVersionRange>?> modelCache, object? sourceIdentifier)
    {
        var constraints = new Dictionary<string, List<Constraint>>(StringComparer.Ordinal);
        var expanded = new HashSet<CkModelId>();
        var queue = new Queue<Constraint>(roots.Select(r => new Constraint(r, null)));
        while (queue.Count > 0)
        {
            var constraint = queue.Dequeue();
            if (!constraints.TryGetValue(constraint.Range.Name, out var list))
            {
                list = [];
                constraints[constraint.Range.Name] = list;
            }

            if (!list.Any(c => c.Origin == constraint.Origin && c.Range.FullName == constraint.Range.FullName))
            {
                list.Add(constraint);
            }

            if (!chosen.TryGetValue(constraint.Range.Name, out var chosenId) || !expanded.Add(chosenId))
            {
                continue;
            }

            if (!modelCache.TryGetValue(chosenId, out var children))
            {
                var model = await catalogManager.TryGetAsync(chosenId, new OperationResult(), sourceIdentifier)
                    .ConfigureAwait(false);
                children = model?.GetResolutionRanges();
                modelCache[chosenId] = children;
            }

            // A model that cannot be loaded here is reported by the resolver's own load; no constraints from it.
            foreach (var child in children ?? [])
            {
                queue.Enqueue(new Constraint(child, chosenId));
            }
        }

        return constraints;
    }

    private readonly record struct Pick(CkModelId? ModelId, bool EveryRangeHasAVersion);

    private async Task<Pick> PickAsync(string name, List<Constraint> constraints,
        Dictionary<string, ModelExistingResult> existingCache, object? sourceIdentifier)
    {
        var candidates = new List<CkModelId>();
        var everyRangeHasAVersion = true;
        foreach (var range in constraints.Select(c => c.Range).GroupBy(r => r.FullName).Select(g => g.First()))
        {
            if (!existingCache.TryGetValue(range.FullName, out var existing))
            {
                existing = await catalogManager.IsExistingAsync(range, sourceIdentifier).ConfigureAwait(false);
                existingCache[range.FullName] = existing;
            }

            if (existing is { Exists: true, ModelId: not null })
            {
                candidates.Add(existing.ModelId);
            }
            else
            {
                everyRangeHasAVersion = false;
            }
        }

        if (!everyRangeHasAVersion)
        {
            return new Pick(null, false);
        }

        // The highest version of the tightest range satisfies all others when any version does; a listing
        // confirms the rare remaining case (and an empty intersection) instead of reasoning about interval bounds.
        var best = HighestSatisfyingAll(candidates, constraints);
        if (best == null)
        {
            var listed = await catalogManager.ListVersionsAsync(name, sourceIdentifier).ConfigureAwait(false);
            best = HighestSatisfyingAll(listed.Select(i => i.ModelId), constraints);
        }

        logger.LogDebug("Dependency '{ModelName}' resolved to '{ModelId}' for constraints {Constraints}", name, best,
            string.Join(", ", constraints.Select(c => c.Range.FullName)));
        return new Pick(best, true);
    }

    private static CkModelId? HighestSatisfyingAll(IEnumerable<CkModelId> versions, List<Constraint> constraints) =>
        versions.Where(v => constraints.All(c => c.Range.IsSatisfiedBy(v))).OrderByDescending(v => v)
            .FirstOrDefault();

    private async Task<string> DescribeVisibleVersionsAsync(string name, object? sourceIdentifier)
    {
        try
        {
            var versions = await catalogManager.ListVersionsAsync(name, sourceIdentifier).ConfigureAwait(false);
            return string.Join("; ", versions
                .GroupBy(v => v.CatalogName)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(v => v.ModelId.Version))}"));
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not list catalog versions of {CkModelName}", name);
            return "<catalog listing failed>";
        }
    }
}
