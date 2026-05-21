using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
///     Interface for a blueprint catalog
/// </summary>
public interface IBlueprintCatalog
{
    /// <summary>
    ///     Returns the order of the catalog that will be used to resolve blueprints.
    ///     The lower the order, the higher the priority.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Returns the name of the catalog, used for identification. Catalog names must be unique.
    /// </summary>
    string CatalogName { get; }

    /// <summary>
    ///     Returns the description of the catalog, used for outputs including configuration information.
    /// </summary>
    string Description { get; }

    /// <summary>
    ///     Returns true if the catalog can be used to publish or update blueprints, otherwise false.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    ///     Returns true if the catalog can be used to read blueprints, otherwise false.
    /// </summary>
    bool CanRead { get; }

    /// <summary>
    ///     Returns true when the catalog ships blueprints that are managed by an OctoMesh service
    ///     (e.g. embedded with the Communication Controller's NuGet package). Service-managed
    ///     blueprints surface in the Studio for visibility but install / update / uninstall actions
    ///     are blocked — the owning service runs the lifecycle automatically. User-installable
    ///     catalogs (local file system, GitHub) return <c>false</c>.
    /// </summary>
    bool IsServiceManaged { get; }

    /// <summary>
    ///     Refreshes the catalog, e.g., by reloading from disk or fetching from a remote source.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns></returns>
    Task RefreshCatalogAsync(object? sourceIdentifier = null);

    /// <summary>
    ///     Returns true, if the defined source identifier is supported by the catalog.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns></returns>
    bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <summary>
    ///     Checks if a blueprint exists in this catalog
    /// </summary>
    /// <param name="blueprintIdVersionRange">The blueprint id with version range</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>A result indicating if the blueprint exists and if yes, which version</returns>
    Task<BlueprintExistingResult> IsExistingAsync(BlueprintIdVersionRange blueprintIdVersionRange,
        object? sourceIdentifier = null);

    /// <summary>
    ///     Checks if a blueprint exists in this catalog
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>True if the blueprint exists in this catalog, otherwise false</returns>
    Task<bool> IsExistingAsync(BlueprintId blueprintId, object? sourceIdentifier = null);

    /// <summary>
    ///     Gets a blueprint by its id
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="operationResult">Operation results that contain validation messages occurred during deserialization.</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>The deserialized and validated blueprint</returns>
    Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Opens a readable stream for a single file inside a blueprint's folder.
    /// </summary>
    /// <remarks>
    ///     This is the canonical way to read files (seed-data, migration scripts) that live alongside a
    ///     blueprint's <c>blueprint.yaml</c>. Catalog implementations are free to back this with whatever
    ///     storage they use — local file system, HTTP, embedded resources — without exposing a path.
    /// </remarks>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="relativePath">Path to the file relative to the blueprint root, e.g.
    ///     <c>seed-data/entities.yaml</c>. Must use forward-slash separators and must not contain
    ///     <c>..</c> or rooted segments.</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>A stream positioned at the start of the file; the caller is responsible for disposing it.</returns>
    Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets the absolute path to a blueprint's directory.
    /// </summary>
    /// <remarks>
    ///     Retained for the publish path (<see cref="PublishAsync" /> needs an on-disk source directory).
    ///     New read code should use <see cref="OpenBlueprintFileAsync" /> instead — embedded-resource and
    ///     remote catalogs cannot return a meaningful filesystem path.
    /// </remarks>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>The absolute path to the blueprint directory</returns>
    [Obsolete("Use OpenBlueprintFileAsync for reading files inside a blueprint. " +
              "This API is retained only for the publish path that needs an on-disk source directory.")]
    string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null);

    /// <summary>
    ///     Publishes a blueprint to the catalog
    /// </summary>
    /// <param name="blueprintMetaRoot">The validated blueprint</param>
    /// <param name="blueprintDirectory">The directory containing the blueprint files</param>
    /// <param name="force">Forces the operation by replacing blueprint files if they exist.</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishAsync(BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory, bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Lists all blueprints in the catalog for the defined source identifier.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>List of blueprints that are available in the catalog</returns>
    IAsyncEnumerable<BlueprintCatalogResultItem> ListAsync(object? sourceIdentifier);

    /// <summary>
    ///     Searches for blueprints in the catalog for the defined source identifier and search term.
    /// </summary>
    /// <param name="searchTerm">Search term to search for in the blueprints</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>List of blueprints that match the search term</returns>
    IAsyncEnumerable<BlueprintCatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier);
}
