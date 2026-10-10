using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkCascadeService" />.
/// </summary>
public class CkCascadeService : ICkCascadeService
{
    private const int MaxParallelLoads = 8;

    private readonly ICatalogService _catalogService;
    private readonly ICkBaselineResolver _baselineResolver;
    private readonly ICkCompatibilityVerdictService _verdictService;
    private readonly ICkSurfaceSatisfactionChecker _checker;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkCascadeService" /> class.
    /// </summary>
    public CkCascadeService(ICatalogService catalogService, ICkBaselineResolver baselineResolver,
        ICkCompatibilityVerdictService verdictService, ICkSurfaceSatisfactionChecker checker)
    {
        _catalogService = catalogService;
        _baselineResolver = baselineResolver;
        _verdictService = verdictService;
        _checker = checker;
    }

    /// <inheritdoc />
    public async Task<CkCascadeResult> AnalyzeAsync(CkCompiledModelRoot candidate, string? catalogName = null)
    {
        var id = candidate.ModelId;
        var baseline = await _baselineResolver.ResolveAsync(id.Name, id.Version, catalogName).ConfigureAwait(false);
        var verdict = baseline.Baseline == null
            ? null
            : await _verdictService.EvaluateAsync(baseline, candidate, catalogName).ConfigureAwait(false);

        var (models, warnings) = await LoadNewestPerMajorAsync(id.Name, catalogName).ConfigureAwait(false);
        var dependents = SelectDependents(id.Name, models);

        var surfaceCandidate = CkSurfaceCandidate.Create(candidate, verdict?.BaselineModel,
            verdict?.ClassifiedChanges ?? [], dependents);
        var checks = dependents.Select(d => _checker.Check(surfaceCandidate, d))
            .OrderBy(c => c.Verdict == CkDependentVerdict.Breaks ? 0 : c.Verdict == CkDependentVerdict.NeedsRepin ? 1 : c.Verdict == CkDependentVerdict.Compatible ? 2 : 3)
            .ThenBy(c => c.Dependent.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Dependent.Version)
            .ToList();

        // A transitive-only dependent is not affected by the candidate itself, but by the dependents it builds on:
        // say which of them break.
        var breaking = new HashSet<string>(checks.Where(c => c.Verdict == CkDependentVerdict.Breaks).Select(c => c.Dependent.Name));
        for (var i = 0; i < checks.Count; i++)
        {
            if (checks[i].Verdict != CkDependentVerdict.Compatible ||
                !checks[i].Reasons.Any(r => r.StartsWith("no direct dependency", StringComparison.Ordinal) ||
                                         r.StartsWith("no references into the candidate", StringComparison.Ordinal)))
            {
                continue;
            }

            var model = dependents.First(d => d.ModelId == checks[i].Dependent);
            var via = (model.Dependencies ?? []).Select(d => d.Name)
                .Concat((model.DependencyRanges ?? []).Select(d => d.Range.Name))
                .Where(breaking.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (via.Count > 0)
            {
                checks[i] = checks[i] with
                {
                    Reasons = [.. checks[i].Reasons, $"builds on {string.Join(", ", via)}, which would break"]
                };
            }
        }

        return new CkCascadeResult
        {
            Candidate = candidate, Baseline = baseline, CandidateVerdict = verdict, Dependents = checks,
            LoadWarnings = warnings
        };
    }

    /// <summary>
    ///     Every model that names a model of the affected set in its dependencies, starting with the candidate's name
    ///     (a fixpoint: a dependent of a dependent is a dependent).
    /// </summary>
    private static List<CkCompiledModelRoot> SelectDependents(string candidateName,
        IReadOnlyList<CkCompiledModelRoot> models)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal) { candidateName };
        var selected = new HashSet<CkCompiledModelRoot>();
        bool changed;
        do
        {
            changed = false;
            foreach (var model in models.Where(m => !selected.Contains(m)))
            {
                var names = (model.Dependencies ?? []).Select(d => d.Name)
                    .Concat((model.DependencyRanges ?? []).Select(d => d.Range.Name));
                if (names.Any(affected.Contains))
                {
                    selected.Add(model);
                    changed |= affected.Add(model.ModelId.Name);
                }
            }
        } while (changed);

        return models.Where(selected.Contains).ToList();
    }

    private async Task<(List<CkCompiledModelRoot> Models, List<string> Warnings)> LoadNewestPerMajorAsync(
        string candidateName, string? catalogName)
    {
        var listed = catalogName != null
            ? await _catalogService.ListAsync(catalogName, 0, int.MaxValue).ConfigureAwait(false)
            : await _catalogService.ListAsync(0, int.MaxValue).ConfigureAwait(false);

        var newest = listed.ModelResultItems
            .Where(i => !string.Equals(i.ModelId.Name, candidateName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => (Name: i.ModelId.Name, i.ModelId.Version.Major))
            .Select(g => g.OrderByDescending(i => i.ModelId.Version).First())
            .ToList();

        var models = new List<CkCompiledModelRoot>();
        var warnings = new List<string>();
        using var gate = new SemaphoreSlim(MaxParallelLoads);
        var tasks = newest.Select(async item =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var operationResult = new OperationResult();
                var model = await _catalogService.GetAsync(item.CatalogName, item.ModelId, operationResult)
                    .ConfigureAwait(false);
                lock (models)
                {
                    if (model == null || operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        warnings.Add($"{item.ModelId.FullName} ({item.CatalogName}) could not be loaded and was not checked.");
                    }
                    else
                    {
                        models.Add(model);
                    }
                }
            }
            catch (Exception ex) when (ex is ModelCatalogException or CompilerException or IOException
                                           or HttpRequestException)
            {
                lock (models)
                {
                    warnings.Add($"{item.ModelId.FullName} ({item.CatalogName}) could not be loaded and was not checked: {ex.Message}");
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        return (models.OrderBy(m => m.ModelId.Name, StringComparer.Ordinal).ThenBy(m => m.ModelId.Version).ToList(),
            warnings.OrderBy(w => w, StringComparer.Ordinal).ToList());
    }
}
