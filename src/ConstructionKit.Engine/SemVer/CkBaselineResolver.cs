using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkBaselineResolver" />. Uses only the read methods of
///     <see cref="ICatalogService" /> (<c>GetCatalogList</c>, <c>IsExistingAsync</c> per catalog), one query per catalog
///     and version range, so the local and the published entries of the same version are never merged.
/// </summary>
public class CkBaselineResolver : ICkBaselineResolver
{
    private const int MaxIgnoredLocalEntries = 1000;

    private readonly ICatalogService _catalogService;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkBaselineResolver" /> class.
    /// </summary>
    public CkBaselineResolver(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <inheritdoc />
    public Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion,
        string? catalogName = null) =>
        ResolveCoreAsync(modelName, declaredVersion,
            catalogName != null
                ? [catalogName]
                : _catalogService.GetCatalogList().Select(c => c.Item1).ToList());

    /// <inheritdoc />
    public Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion,
        CkBaselineSource source) =>
        ResolveCoreAsync(modelName, declaredVersion,
            _catalogService.GetCatalogList().Select(c => c.Item1)
                .Where(c => source == CkBaselineSource.Local || !IsLocalCatalog(c)).ToList());

    private async Task<CkBaselineResolution> ResolveCoreAsync(string modelName, CkVersion declaredVersion,
        List<string> catalogs)
    {
        var majorStart = new CkVersion(declaredVersion.Major, 0, 0);
        var nextMajorStart = new CkVersion(declaredVersion.Major + 1, 0, 0);
        var sourceUnreachable = false;
        var ignored = new List<CkModelId>();

        // Rule 1 + 3: newest version of the declared major; local entries only below the declared version.
        var candidates = new List<Candidate>();
        for (var order = 0; order < catalogs.Count; order++)
        {
            var catalog = catalogs[order];
            var isLocal = IsLocalCatalog(catalog);
            var upper = isLocal ? declaredVersion : nextMajorStart;
            if (upper.CompareTo(majorStart) > 0)
            {
                var result = await QueryAsync(catalog, modelName, majorStart, upper);
                sourceUnreachable |= result.SourceUnreachable;
                AddCandidate(candidates, result, catalog, isLocal, order);
            }

            if (isLocal)
            {
                ignored.AddRange(await ListLocalEntriesAsync(catalog, modelName, declaredVersion, nextMajorStart));
            }
        }

        var isFromLowerMajor = false;
        // An unreachable source may hide the same-major baseline: never fall back to a lower major then (OCTO-CK102).
        if (candidates.Count == 0 && !sourceUnreachable && declaredVersion.Major > 0)
        {
            // Rule 2: a new major is diffed against the newest version of the highest lower major.
            for (var order = 0; order < catalogs.Count; order++)
            {
                var catalog = catalogs[order];
                var result = await QueryAsync(catalog, modelName, new CkVersion(0, 0, 0), majorStart);
                sourceUnreachable |= result.SourceUnreachable;
                AddCandidate(candidates, result, catalog, IsLocalCatalog(catalog), order);
            }

            isFromLowerMajor = candidates.Count > 0;
        }

        // Highest version wins; on a tie a published (non-local) entry beats a local copy, then catalog order.
        var best = candidates
            .OrderByDescending(c => c.ModelId.Version)
            .ThenBy(c => c.IsLocal)
            .ThenBy(c => c.Order)
            .FirstOrDefault();

        return new CkBaselineResolution
        {
            ModelName = modelName,
            DeclaredVersion = declaredVersion,
            Baseline = best?.ModelId,
            CatalogName = best?.CatalogName,
            IsLocal = best?.IsLocal ?? false,
            IsFromLowerMajor = isFromLowerMajor,
            CacheUpdatedAt = best?.CacheUpdatedAt,
            SourceUnreachable = sourceUnreachable,
            IgnoredLocalEntries = ignored.OrderBy(id => id.Version).ToList()
        };
    }

    /// <summary>
    ///     True for the local file-system catalog: its entries come from local builds, never from a publish.
    /// </summary>
    public static bool IsLocalCatalog(string catalogName) =>
        string.Equals(catalogName, LocalFileSystemCatalog.Name, StringComparison.OrdinalIgnoreCase);

    private Task<ModelExistingResult> QueryAsync(string catalog, string modelName, CkVersion fromInclusive,
        CkVersion toExclusive) =>
        _catalogService.IsExistingAsync(catalog,
            new CkModelIdVersionRange(modelName, $"[{fromInclusive},{toExclusive})"));

    /// <summary>
    ///     Lists the local entries in <c>[from, to)</c>, newest first, by narrowing the upper bound after each hit (the
    ///     local catalog answers range lookups from the files on disk).
    /// </summary>
    private async Task<List<CkModelId>> ListLocalEntriesAsync(string catalog, string modelName, CkVersion from,
        CkVersion to)
    {
        var entries = new List<CkModelId>();
        var upper = to;
        while (upper.CompareTo(from) > 0 && entries.Count < MaxIgnoredLocalEntries)
        {
            var result = await QueryAsync(catalog, modelName, from, upper);
            if (!result.Exists || result.ModelId == null || result.ModelId.Version.CompareTo(upper) >= 0)
            {
                break;
            }

            entries.Add(result.ModelId);
            upper = result.ModelId.Version;
        }

        return entries;
    }

    private static void AddCandidate(List<Candidate> candidates, ModelExistingResult result, string catalog,
        bool isLocal, int order)
    {
        if (result is { Exists: true, ModelId: not null })
        {
            candidates.Add(new Candidate(result.ModelId, result.CatalogName ?? catalog, isLocal, order,
                result.CacheUpdatedAt));
        }
    }

    private sealed record Candidate(
        CkModelId ModelId,
        string CatalogName,
        bool IsLocal,
        int Order,
        DateTime? CacheUpdatedAt);
}
