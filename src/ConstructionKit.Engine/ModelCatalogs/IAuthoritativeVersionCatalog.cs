using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     The versions a catalog holds according to its authoritative source (AB#6580), read at call time.
/// </summary>
/// <remarks>
///     <see cref="SourceUnreachable" /> is the only way to tell "the source could not be asked" from "the catalog has no such
///     model": when it is true, <see cref="Versions" /> is empty and means nothing. An empty list with the flag false is a
///     genuine answer (the repository exists and does not hold the model).
/// </remarks>
/// <param name="Versions">Every version of the matching models (all spellings of the name), ascending.</param>
/// <param name="SourceUnreachable">True when the authoritative source could not be read or answered inconsistently.</param>
/// <param name="UnreachableReason">Why the source counts as unreachable (shown in <c>OCTO-CK202</c>), or null.</param>
/// <param name="ReadAt">When the source was read.</param>
public sealed record CatalogVersionsResult(
    IReadOnlyList<CkModelId> Versions,
    bool SourceUnreachable,
    string? UnreachableReason,
    DateTime ReadAt)
{
    /// <summary>A genuine answer: the source was read and holds exactly these versions.</summary>
    public static CatalogVersionsResult Found(IEnumerable<CkModelId> versions) =>
        new(versions.Distinct().OrderBy(v => v.Version).ThenBy(v => v.Name, StringComparer.Ordinal).ToList(), false,
            null, DateTime.UtcNow);

    /// <summary>The source could not be read; the (empty) version list must not be interpreted.</summary>
    public static CatalogVersionsResult Unreachable(string reason) => new([], true, reason, DateTime.UtcNow);
}

/// <summary>
///     Optional capability of a catalog whose cheap read path can lag behind its repository (the GitHub catalogs read a
///     Pages mirror): reads the versions of a model from the source of truth, authenticated as the publisher already is
///     (AB#6580). The publish gate and the unpublish dependents check use it instead of the cache, so a lagging or
///     failing mirror can neither hide a published version (stale baseline, AB#6581) nor look like an empty catalog (a
///     breaking version published as a "first publication", AB#6580). A catalog that does not implement the interface is
///     read through its cache as before.
/// </summary>
public interface IAuthoritativeVersionCatalog
{
    /// <summary>
    ///     Reads the versions of <paramref name="modelName" /> (matched case-insensitively, every spelling) from the
    ///     source of truth, or of all models when it is null. Never reports a read failure as an empty catalog: any error,
    ///     rate limit, timeout, missing repository or inconsistent index is <see cref="CatalogVersionsResult.SourceUnreachable" />.
    /// </summary>
    Task<CatalogVersionsResult> ReadVersionsAsync(string? modelName, CancellationToken? cancellationToken = null);
}
