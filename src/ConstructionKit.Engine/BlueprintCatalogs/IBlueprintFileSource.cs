namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     The files of one blueprint draft, read by <see cref="IBlueprintDraftValidator" /> (AB#9371). Paths are relative
///     to the blueprint root (the folder that holds <c>blueprint.yaml</c>) and always use <c>/</c>.
/// </summary>
/// <remarks>
///     Implementations: <see cref="DirectoryBlueprintFileSource" /> (octo-bpm), <see cref="InMemoryBlueprintFileSource" />
///     (a zip or inline files uploaded to the asset repository) and <see cref="CatalogBlueprintFileSource" /> (a blueprint
///     of a blueprint catalog, install path).
/// </remarks>
public interface IBlueprintFileSource
{
    /// <summary>
    ///     Reads a file as text.
    /// </summary>
    /// <param name="relativePath">Path relative to the blueprint root, with <c>/</c> as separator</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The content, or <c>null</c> when the file does not exist</returns>
    Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Lists the files below a folder (recursively), relative to the blueprint root with <c>/</c> as separator.
    /// </summary>
    /// <param name="relativeDirectory">Folder relative to the blueprint root</param>
    /// <returns>The files, or <c>null</c> when the source cannot list files (a catalog blueprint)</returns>
    IReadOnlyList<string>? ListFiles(string relativeDirectory);
}
