using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     What a catalog records about a version number that was unpublished (AB#5433): the number is burned.
/// </summary>
/// <param name="WithdrawnAt">When the version was unpublished.</param>
/// <param name="Reason">The reason given at unpublish, or null.</param>
public sealed record WithdrawnVersionInfo(DateTime WithdrawnAt, string? Reason);

/// <summary>
///     Optional capability of a writable catalog (AB#6389): tells whether a version number is burned, so the publish
///     gate can report it together with its other reasons instead of the catalog throwing at write time. The write
///     still checks as well (the race and the callers that bypass the gate). A catalog that does not implement the
///     interface reports nothing in the gate.
/// </summary>
public interface IWithdrawnVersionCatalog
{
    /// <summary>The withdrawn record of the version in either catalog root, or null when the number is not burned.</summary>
    Task<WithdrawnVersionInfo?> FindWithdrawnVersionAsync(CkModelId modelId);
}
