using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkCatalogValidationService" />.
/// </summary>
public class CkCatalogValidationService : ICkCatalogValidationService
{
    private const int MaxParallelLoads = 8;

    private readonly ICatalogService _catalogService;
    private readonly ICkModelDiffService _diffService;
    private readonly ICkSemVerClassifier _classifier;
    private readonly ICkSurfaceSatisfactionChecker _checker;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkCatalogValidationService" /> class.
    /// </summary>
    public CkCatalogValidationService(ICatalogService catalogService, ICkModelDiffService diffService,
        ICkSemVerClassifier classifier, ICkSurfaceSatisfactionChecker checker)
    {
        _catalogService = catalogService;
        _diffService = diffService;
        _classifier = classifier;
        _checker = checker;
    }

    /// <summary>
    ///     True for the catalogs run by Meshmakers (the private and the public GitHub catalog): a model that does not
    ///     resolve there is an error. Any other catalog only gets warnings.
    /// </summary>
    public static bool IsMeshmakersRunCatalog(string catalogName) =>
        string.Equals(catalogName, "PrivateGitHubCatalog", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(catalogName, "PublicGitHubCatalog", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<CkCatalogValidationResult> ValidateAsync(string catalogName,
        IReadOnlyCollection<string>? models = null)
    {
        var listed = await _catalogService.ListAsync(catalogName, 0, int.MaxValue).ConfigureAwait(false);
        var items = listed.ModelResultItems.Select(i => i.ModelId).Distinct().ToList();

        // Newest version per exact name (a case-only variant is a model of its own, and a defect).
        var newest = items.GroupBy(i => i.Name, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(i => i.Version).First())
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ToList();
        var cache = new ModelCache(_catalogService, catalogName);

        var (selected, scopeNames) = await SelectScopeAsync(catalogName, newest, models, cache).ConfigureAwait(false);
        await cache.LoadAsync(selected).ConfigureAwait(false);

        var checks = new List<CkCatalogModelCheck>();
        foreach (var id in selected)
        {
            var model = cache.TryGet(id);
            checks.Add(model == null
                ? new CkCatalogModelCheck
                {
                    Model = id, IsRangeRetaining = false, IsResolved = false,
                    Findings = [$"{id.FullName} could not be loaded from the catalog: {cache.Error(id)}"]
                }
                : await CheckAsync(catalogName, model, items, cache).ConfigureAwait(false));
        }

        var variants = await FindCaseVariantsAsync(newest, scopeNames, cache).ConfigureAwait(false);
        return new CkCatalogValidationResult
        {
            CatalogName = catalogName,
            DefectsAreErrors = IsMeshmakersRunCatalog(catalogName),
            Models = checks.OrderBy(c => c.IsResolved ? 1 : 0).ThenBy(c => c.Model.Name, StringComparer.Ordinal).ToList(),
            CaseVariants = variants
        };
    }

    /// <summary>
    ///     The models to check: the whole catalog, or the named models and their transitive dependents.
    /// </summary>
    private static async Task<(List<CkModelId> Selected, HashSet<string>? ScopeNames)> SelectScopeAsync(
        string catalogName, List<CkModelId> newest, IReadOnlyCollection<string>? names, ModelCache cache)
    {
        if (names == null || names.Count == 0)
        {
            return (newest, null);
        }

        var known = newest.Select(i => i.Name).ToList();
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!known.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ModelValidationException(
                    $"Model '{name}' is not in catalog '{catalogName}'. Models in the catalog: " +
                    string.Join(", ", known.Take(40)) + (known.Count > 40 ? ", ..." : "") + ".");
            }

            requested.Add(name);
        }

        // Dependents: load the dependency names of every model (the newest version) and take the fixpoint.
        await cache.LoadAsync(newest).ConfigureAwait(false);
        var affected = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var id in newest)
            {
                if (affected.Contains(id.Name))
                {
                    continue;
                }

                var model = cache.TryGet(id);
                if (model != null && DependencyNames(model).Any(affected.Contains))
                {
                    affected.Add(id.Name);
                    changed = true;
                }
            }
        } while (changed);

        return (newest.Where(i => affected.Contains(i.Name)).ToList(), affected);
    }

    private static IEnumerable<string> DependencyNames(CkCompiledModelRoot model) =>
        model.IsRangeRetaining
            ? model.DependencyRanges!.Select(d => d.Range.Name)
            : (model.Dependencies ?? []).Select(d => d.Name);

    private async Task<CkCatalogModelCheck> CheckAsync(string catalogName, CkCompiledModelRoot model,
        IReadOnlyList<CkModelId> allVersions, ModelCache cache)
    {
        var findings = new List<string>();
        var notes = new List<string>();
        var surfaceChecks = new List<CkDependentCheck>();
        var id = model.ModelId;

        if (model.IsRangeRetaining)
        {
            foreach (var entry in model.DependencyRanges!)
            {
                await CheckRangeAsync(catalogName, model, entry, allVersions, cache, findings, notes, surfaceChecks)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            foreach (var pin in model.Dependencies ?? [])
            {
                await CheckPinAsync(model, pin, allVersions, cache, findings, notes).ConfigureAwait(false);
            }
        }

        if (!(model.IsRangeRetaining ? model.DependencyRanges!.Count > 0 : (model.Dependencies?.Count ?? 0) > 0))
        {
            notes.Add("no dependencies");
        }

        return new CkCatalogModelCheck
        {
            Model = id, IsRangeRetaining = model.IsRangeRetaining, IsResolved = findings.Count == 0,
            Findings = findings, Notes = notes, SurfaceChecks = surfaceChecks
        };
    }

    private static async Task CheckPinAsync(CkCompiledModelRoot model, CkModelId pin,
        IReadOnlyList<CkModelId> allVersions, ModelCache cache, List<string> findings, List<string> notes)
    {
        var versions = allVersions
            .Where(v => string.Equals(v.Name, pin.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var listed = versions.FirstOrDefault(v => v.Version.CompareTo(pin.Version) == 0);
        if (listed != null)
        {
            // AB#6582: the index listing the pinned version is not enough - the file it points to must load. A pinned file
            // deleted by hand with its index entry kept resolves in no tenant.
            if (await cache.GetAsync(listed).ConfigureAwait(false) == null)
            {
                findings.Add($"{model.ModelId.FullName} pins {pin.FullName}, which the catalog index lists, but the " +
                             $"model file could not be loaded: {cache.Error(listed)}");
                return;
            }

            notes.Add($"{pin.FullName} is in the catalog");
            return;
        }

        findings.Add(versions.Count == 0
            ? $"{model.ModelId.FullName} pins {pin.FullName}, but the catalog has no version of {pin.Name}"
            : $"{model.ModelId.FullName} pins {pin.FullName}, which is not in the catalog " +
              $"(available: {string.Join(", ", versions.OrderBy(v => v.Version).Select(v => v.Version.ToString()))})");
    }

    private async Task CheckRangeAsync(string catalogName, CkCompiledModelRoot model, CkModelDependencyDto entry,
        IReadOnlyList<CkModelId> allVersions, ModelCache cache, List<string> findings, List<string> notes,
        List<CkDependentCheck> surfaceChecks)
    {
        var name = entry.Range.Name;
        var effective = entry.GetEffectiveRange();
        var versions = allVersions.Where(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Version).ToList();
        if (versions.Count == 0)
        {
            findings.Add($"{model.ModelId.FullName} depends on {name} {entry.Range.ModelVersionRange}, but the catalog has no version of {name}");
            return;
        }

        var admitted = versions.Where(v => effective.ModelVersionRange.IsSatisfiedBy(v.Version)).ToList();
        var newest = versions.Last();
        if (admitted.Count == 0)
        {
            findings.Add($"{model.ModelId.FullName} depends on {name} {effective.ModelVersionRange}, which no version in the " +
                         $"catalog satisfies (available: {string.Join(", ", versions.Select(v => v.Version.ToString()))})");
            return;
        }

        var candidateId = admitted.Last();
        if (newest.Version.CompareTo(candidateId.Version) > 0)
        {
            notes.Add($"not in range: {newest.FullName} is outside {name} {effective.ModelVersionRange}; " +
                      $"checked against {candidateId.FullName}");
        }

        var candidate = await cache.GetAsync(candidateId).ConfigureAwait(false);
        if (candidate == null)
        {
            findings.Add($"{candidateId.FullName} (a dependency of {model.ModelId.FullName}) could not be loaded: {cache.Error(candidateId)}");
            return;
        }

        // The baseline of the dependency is its predecessor in the catalog, the same one a dry run of this version
        // would have used before it was published (so ValidateCatalog and ValidateCascade agree).
        var predecessor = Predecessor(versions, candidateId);
        CkCompiledModelRoot? baselineModel = null;
        if (predecessor != null)
        {
            baselineModel = await cache.GetAsync(predecessor).ConfigureAwait(false);
        }

        var classified = baselineModel == null
            ? []
            : _classifier.Classify(_diffService.Diff(baselineModel, candidate), baselineModel, candidate);
        var surfaceCandidate = CkSurfaceCandidate.Create(candidate, baselineModel, classified, [model]);
        var check = _checker.Check(surfaceCandidate, model);
        surfaceChecks.Add(check);
        switch (check.Verdict)
        {
            case CkDependentVerdict.Breaks:
                findings.AddRange(check.Reasons);
                break;
            case CkDependentVerdict.NotInRange:
                notes.AddRange(check.Reasons);
                break;
            default:
                notes.AddRange(check.Reasons.Select(r => $"{candidateId.FullName}: {r}"));
                break;
        }
    }

    /// <summary>
    ///     The version a release is compared with: the newest version below it in its major; the first release of a
    ///     major is compared with the newest version of the highest lower major (the rules of the baseline resolver).
    /// </summary>
    internal static CkModelId? Predecessor(IReadOnlyList<CkModelId> versionsAscending, CkModelId candidate) =>
        versionsAscending.LastOrDefault(v => v.Version.Major == candidate.Version.Major &&
                                             v.Version.CompareTo(candidate.Version) < 0) ??
        versionsAscending.LastOrDefault(v => v.Version.Major < candidate.Version.Major);

    private async Task<IReadOnlyList<CkCaseVariantGroup>> FindCaseVariantsAsync(List<CkModelId> newest,
        HashSet<string>? scopeNames, ModelCache cache)
    {
        var groups = newest.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1 && (scopeNames == null || scopeNames.Contains(g.Key)))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<CkCaseVariantGroup>();
        foreach (var group in groups)
        {
            var variants = new List<CkCaseVariant>();
            foreach (var id in group.OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                var model = await cache.GetAsync(id).ConfigureAwait(false);
                var path = model == null
                    ? "(root unknown: model could not be loaded)"
                    : CkCatalogLayout.ModelDirectory(CkCatalogLayout.GetPublishRoot(model), id.Name);
                variants.Add(new CkCaseVariant(id.Name, id.Version, path));
            }

            result.Add(new CkCaseVariantGroup(variants));
        }

        return result;
    }

    /// <summary>Loads each model of the catalog at most once, in parallel.</summary>
    private sealed class ModelCache(ICatalogService catalogService, string catalogName)
    {
        private readonly Dictionary<CkModelId, CkCompiledModelRoot?> _models = new();
        private readonly Dictionary<CkModelId, string> _errors = new();
        private readonly object _lock = new();

        public CkCompiledModelRoot? TryGet(CkModelId id)
        {
            lock (_lock)
            {
                return _models.TryGetValue(id, out var model) ? model : null;
            }
        }

        public string Error(CkModelId id)
        {
            lock (_lock)
            {
                return _errors.TryGetValue(id, out var error) ? error : "unknown error";
            }
        }

        public async Task<CkCompiledModelRoot?> GetAsync(CkModelId id)
        {
            lock (_lock)
            {
                if (_models.TryGetValue(id, out var cached))
                {
                    return cached;
                }
            }

            try
            {
                var operationResult = new OperationResult();
                var model = await catalogService.GetAsync(catalogName, id, operationResult).ConfigureAwait(false);
                if (model == null || operationResult.HasErrors || operationResult.HasFatalErrors)
                {
                    Store(id, null, "the model file could not be read");
                    return null;
                }

                Store(id, model, null);
                return model;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Store(id, null, ex.Message);
                return null;
            }
        }

        public async Task LoadAsync(IEnumerable<CkModelId> ids)
        {
            using var gate = new SemaphoreSlim(MaxParallelLoads);
            await Task.WhenAll(ids.Select(async id =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await GetAsync(id).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
        }

        private void Store(CkModelId id, CkCompiledModelRoot? model, string? error)
        {
            lock (_lock)
            {
                _models[id] = model;
                if (error != null)
                {
                    _errors[id] = error;
                }
            }
        }
    }
}
