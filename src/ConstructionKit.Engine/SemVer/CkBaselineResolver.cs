using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
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
    private readonly IReadOnlyList<ICatalog> _catalogs;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkBaselineResolver" /> class.
    /// </summary>
    /// <param name="catalogService">The read side of the catalogs (cache-backed).</param>
    /// <param name="catalogs">
    ///     The catalogs themselves. A catalog that implements <see cref="IAuthoritativeVersionCatalog" /> is read at its
    ///     source of truth for the publish baseline (AB#6580); without this list every catalog is read through its cache.
    /// </param>
    public CkBaselineResolver(ICatalogService catalogService, IEnumerable<ICatalog>? catalogs = null)
    {
        _catalogService = catalogService;
        _catalogs = (catalogs ?? []).ToList();
    }

    /// <inheritdoc />
    public Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion,
        string? catalogName = null) =>
        ResolveCoreAsync(modelName, declaredVersion,
            catalogName != null
                ? [catalogName]
                : _catalogService.GetCatalogList().Select(c => c.Item1).ToList());

    /// <inheritdoc />
    public async Task<CkBaselineResolution> ResolveAsync(string modelName, CkVersion declaredVersion,
        CkBaselineSource source)
    {
        var catalogs = _catalogService.GetCatalogList().Select(c => c.Item1)
            .Where(c => source == CkBaselineSource.Local || !IsLocalCatalog(c)).ToList();
        var resolution = await ResolveCoreAsync(modelName, declaredVersion, catalogs).ConfigureAwait(false);

        // Remote with no readable catalog beyond the embedded one (e.g. both GitHub catalogs disabled by a missing
        // token) cannot tell "first publication" from "nothing could be asked": treat it as unreachable (OCTO-CK202).
        if (source == CkBaselineSource.Remote && resolution.Baseline == null && !resolution.SourceUnreachable &&
            !catalogs.Any(c => !string.Equals(c, "EmbeddedResourceCatalog", StringComparison.OrdinalIgnoreCase)))
        {
            return resolution with { SourceUnreachable = true };
        }

        return resolution;
    }

    /// <inheritdoc />
    public async Task<CkBaselineResolution> ResolvePublishBaselineAsync(string modelName, CkVersion declaredVersion,
        string targetCatalog)
    {
        List<CkModelId> known;
        bool sourceUnreachable;
        string? unreachableReason = null;
        DateTime? cacheUpdatedAt;
        if (_catalogs.FirstOrDefault(c => string.Equals(c.CatalogName, targetCatalog, StringComparison.OrdinalIgnoreCase))
            is IAuthoritativeVersionCatalog authoritative)
        {
            // AB#6580/AB#6581: the publish baseline is read from the repository of the catalog, not from the cache
            // (which is filled from a mirror that lags and can answer 404). An unreadable source leaves no baseline at
            // all - the gate refuses with OCTO-CK202, it never sees an empty list.
            var read = await authoritative.ReadVersionsAsync(modelName).ConfigureAwait(false);
            sourceUnreachable = read.SourceUnreachable;
            unreachableReason = read.UnreachableReason;
            cacheUpdatedAt = read.ReadAt;
            known = sourceUnreachable
                ? []
                : read.Versions
                    .Where(id => string.Equals(id.Name, modelName, StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .OrderBy(id => id.Version)
                    .ThenBy(id => id.Name, StringComparer.Ordinal)
                    .ToList();
        }
        else
        {
            var spellings = await GetSpellingsAsync(targetCatalog, modelName);
            var listing = await _catalogService.ListAsync(targetCatalog, 0, int.MaxValue);
            known = (listing?.ModelResultItems ?? [])
                .Select(i => i.ModelId)
                .Where(id => string.Equals(id.Name, modelName, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(id => id.Version)
                .ThenBy(id => id.Name, StringComparer.Ordinal)
                .ToList();

            // One range query for the reachability flag and the cache timestamp of the target catalog.
            var majorStart = new CkVersion(declaredVersion.Major, 0, 0);
            var probe = await QueryAsync(targetCatalog, spellings, majorStart,
                new CkVersion(declaredVersion.Major + 1, 0, 0));
            sourceUnreachable = probe.SourceUnreachable;
            cacheUpdatedAt = probe.CacheUpdatedAt;
        }

        var declaredEntry = known.FirstOrDefault(id => id.Version.CompareTo(declaredVersion) == 0);
        var others = known.Where(id => id.Version.CompareTo(declaredVersion) != 0).ToList();
        var sameMajor = others.Where(id => id.Version.Major == declaredVersion.Major &&
                                           (declaredEntry == null || id.Version.CompareTo(declaredVersion) < 0))
            .ToList();
        var isFromLowerMajor = false;
        CkModelId? baseline = sameMajor.LastOrDefault();
        // An unreachable source may hide the same-major baseline: never fall back to a lower major then (OCTO-CK102).
        if (baseline == null && !sourceUnreachable && declaredVersion.Major > 0)
        {
            baseline = others.LastOrDefault(id => id.Version.Major < declaredVersion.Major);
            isFromLowerMajor = baseline != null;
        }

        return new CkBaselineResolution
        {
            ModelName = modelName,
            DeclaredVersion = declaredVersion,
            Baseline = baseline,
            CatalogName = baseline == null ? null : targetCatalog,
            IsLocal = false,
            IsFromLowerMajor = isFromLowerMajor,
            CacheUpdatedAt = cacheUpdatedAt,
            SourceUnreachable = sourceUnreachable,
            SourceUnreachableReason = unreachableReason,
            DeclaredVersionPublished = declaredEntry,
            KnownVersions = known
        };
    }

    private async Task<CkBaselineResolution> ResolveCoreAsync(string modelName, CkVersion declaredVersion,
        List<string> catalogs)
    {
        var majorStart = new CkVersion(declaredVersion.Major, 0, 0);
        var nextMajorStart = new CkVersion(declaredVersion.Major + 1, 0, 0);
        var sourceUnreachable = false;
        var ignored = new List<CkModelId>();

        // Rule 1 + 3: newest version of the declared major; local entries only below the declared version.
        var candidates = new List<Candidate>();
        var spellings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var order = 0; order < catalogs.Count; order++)
        {
            var catalog = catalogs[order];
            var isLocal = IsLocalCatalog(catalog);
            var names = await GetSpellingsAsync(catalog, modelName);
            spellings[catalog] = names;
            var upper = isLocal ? declaredVersion : nextMajorStart;
            if (upper.CompareTo(majorStart) > 0)
            {
                var result = await QueryAsync(catalog, names, majorStart, upper);
                sourceUnreachable |= result.SourceUnreachable;
                AddCandidate(candidates, result, catalog, isLocal, order);
            }

            if (isLocal)
            {
                ignored.AddRange(await ListLocalEntriesAsync(catalog, names, declaredVersion, nextMajorStart));
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
                var result = await QueryAsync(catalog, spellings[catalog], new CkVersion(0, 0, 0), majorStart);
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

    /// <summary>
    ///     Asks one catalog for the newest version in <c>[from, to)</c> under any of the given spellings of the model name
    ///     and keeps the highest hit; the unreachable flag is the OR over all answers.
    /// </summary>
    private async Task<ModelExistingResult> QueryAsync(string catalog, IReadOnlyList<string> spellings,
        CkVersion fromInclusive, CkVersion toExclusive)
    {
        ModelExistingResult? best = null;
        var unreachable = false;
        foreach (var name in spellings)
        {
            var result = await _catalogService.IsExistingAsync(catalog,
                new CkModelIdVersionRange(name, $"[{fromInclusive},{toExclusive})"));
            unreachable |= result.SourceUnreachable;
            if (result is { Exists: true, ModelId: not null } &&
                (best?.ModelId == null || result.ModelId.Version.CompareTo(best.ModelId.Version) > 0))
            {
                best = result;
            }
        }

        return (best ?? new ModelExistingResult { Exists = false, CatalogName = catalog }) with
        {
            SourceUnreachable = unreachable
        };
    }

    /// <summary>
    ///     AB#6386 (condition of the F2.2 acceptance, Odin 2026-10-10): the catalogs match model names case-sensitively,
    ///     so a model published as <c>System.Bot</c> was invisible to a lookup of <c>system.bot</c> and looked like a first
    ///     publication. This is the one place that discovers the other spellings of a model name in a catalog; every
    ///     baseline lookup (compile gate, <c>ValidateVersion</c>, publish gate) goes through it. The exact spelling
    ///     always comes first.
    /// </summary>
    internal async Task<IReadOnlyList<string>> GetSpellingsAsync(string catalog, string modelName)
    {
        var result = await _catalogService.ListAsync(catalog, 0, int.MaxValue);
        var names = new List<string> { modelName };
        foreach (var item in result?.ModelResultItems ?? [])
        {
            var name = item.ModelId.Name;
            if (string.Equals(name, modelName, StringComparison.OrdinalIgnoreCase) && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    ///     Lists the local entries in <c>[from, to)</c>, newest first, by narrowing the upper bound after each hit (the
    ///     local catalog answers range lookups from the files on disk).
    /// </summary>
    private async Task<List<CkModelId>> ListLocalEntriesAsync(string catalog, IReadOnlyList<string> spellings,
        CkVersion from, CkVersion to)
    {
        var entries = new List<CkModelId>();
        var upper = to;
        while (upper.CompareTo(from) > 0 && entries.Count < MaxIgnoredLocalEntries)
        {
            var result = await QueryAsync(catalog, spellings, from, upper);
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
