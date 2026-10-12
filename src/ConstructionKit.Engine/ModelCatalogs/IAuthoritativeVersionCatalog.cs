using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

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
    /// <summary>
    ///     What the read saw of the indexes that make a version visible (AB#6584), or null. A publish that is judged
    ///     against these versions hands it back to the catalog, which writes only when those indexes are still as read.
    /// </summary>
    public CatalogStateToken? State { get; init; }

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

/// <summary>
///     The state of the version indexes a verdict was computed against (AB#6584): index path to a hash of its content.
///     A path that is absent from <see cref="IndexHashes" /> was not there when the verdict was computed.
/// </summary>
/// <remarks>
///     This is the expected value of the compare-and-swap of a publish: the catalog makes a version visible by writing
///     its per-major index, and does so only when that index is still exactly what the verdict saw. A publish that
///     landed in between changes the index, so the later one is aborted, re-read and re-judged against the earlier.
/// </remarks>
public sealed record CatalogStateToken(IReadOnlyDictionary<string, string> IndexHashes)
{
    /// <summary>A stable hash of the text of an index file; null for a file that does not exist.</summary>
    public static string? Hash(string? content)
    {
        if (content == null)
        {
            return null;
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content))).Replace("-", "");
    }

    /// <summary>True when <paramref name="currentContent" /> is the content the index had when the token was taken.</summary>
    public bool Matches(string path, string? currentContent) =>
        (IndexHashes.TryGetValue(path, out var expected) ? expected : null) == Hash(currentContent);
}

/// <summary>
///     Optional capability of a catalog that can make the write of a version conditional on the state its verdict was
///     computed against (AB#6584).
/// </summary>
public interface IGuardedPublishCatalog
{
    /// <summary>
    ///     Publishes like <see cref="Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog.PublishAsync" />, but
    ///     makes the version visible only if the indexes are still as described by <paramref name="expectedState" />.
    /// </summary>
    /// <exception cref="CatalogChangedDuringPublishException">
    ///     The catalog changed since the state was read; nothing of this publish is visible.
    /// </exception>
    Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force, CatalogStateToken expectedState,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);
}

/// <summary>
///     The catalog changed between the read a verdict was computed from and the write (AB#6584): another publish landed
///     first. Nothing of the aborted publish is visible; the caller re-reads and judges again.
/// </summary>
public sealed class CatalogChangedDuringPublishException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CatalogChangedDuringPublishException(string catalogName, CkModelId modelId, string indexPath)
        : base($"Catalog '{catalogName}' changed while '{modelId.FullName}' was being published: index '{indexPath}' is no " +
               "longer the one the compatibility verdict was computed from. Nothing of this publish is visible.")
    {
    }
}
