using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Manages blueprint catalogs
/// </summary>
public interface IBlueprintCatalogManager
{
    /// <summary>
    /// Searches for blueprints in all known catalogs
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <param name="skip">Amount of blueprints to skip</param>
    /// <param name="take">Amount of blueprints to take</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A search result containing the blueprints found</returns>
    Task<BlueprintSearchResult> SearchAsync(string searchTerm, int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Lists blueprints in all known catalogs.
    /// </summary>
    /// <param name="skip">Amount of blueprints to skip</param>
    /// <param name="take">Amount of blueprints to take</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list result containing the blueprints found</returns>
    Task<BlueprintListResult> ListAsync(int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Tries to look up a blueprint by its id in all known catalogs
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="operationResult">Operation results</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blueprint if found, null otherwise</returns>
    Task<BlueprintMetaRootDto?> TryGetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Looks up a blueprint by its id
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="operationResult">Operation results</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The blueprint</returns>
    Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Opens a readable stream for a file inside a blueprint's folder (e.g. seed data, migration script).
    /// </summary>
    /// <remarks>
    /// Resolves through the same priority order as <see cref="GetAsync" />: catalogs are queried by
    /// ascending <c>Order</c> and the first one carrying the blueprint serves the file. The returned
    /// stream is positioned at the start of the file; the caller is responsible for disposing it.
    /// Throws when the blueprint is missing from every catalog or when the file does not exist inside
    /// the blueprint — use <see cref="TryOpenBlueprintFileAsync" /> for soft-not-found semantics.
    /// </remarks>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="relativePath">Path to the file relative to the blueprint root, e.g.
    /// <c>seed-data/entities.yaml</c>. Must use forward-slash separators and must not contain
    /// <c>..</c> or rooted segments.</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>A stream positioned at the start of the file.</returns>
    Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-not-found variant of <see cref="OpenBlueprintFileAsync" /> that returns <c>null</c> when
    /// either the blueprint or the requested file is missing. Use this for validation paths or
    /// optional content (e.g. checking whether a seed-data file is shipped before trying to import).
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="relativePath">Path to the file relative to the blueprint root.</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>A stream positioned at the start of the file, or <c>null</c> when missing.</returns>
    Task<Stream?> TryOpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the absolute path to a blueprint's directory.
    /// </summary>
    /// <remarks>
    /// Retained for the publish path. New read code should use <see cref="OpenBlueprintFileAsync" />.
    /// </remarks>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <returns>The path to the blueprint directory</returns>
    [Obsolete("Use OpenBlueprintFileAsync for reading files inside a blueprint. " +
              "This API is retained only for the publish path that needs an on-disk source directory.")]
    Task<string> GetBlueprintPathAsync(BlueprintId blueprintId, object? sourceIdentifier = null);

    /// <summary>
    /// Returns a list of known blueprint catalogs
    /// </summary>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <returns>Returns a tuple with name and description of catalog</returns>
    IEnumerable<Tuple<string, string>> GetCatalogList(object? sourceIdentifier = null);

    /// <summary>
    /// Publishes a blueprint to a catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog</param>
    /// <param name="blueprintMetaRoot">Blueprint metadata</param>
    /// <param name="blueprintDirectory">Directory containing blueprint files</param>
    /// <param name="isForced">When true, existing blueprints are replaced</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync(string catalogName, BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory,
        bool isForced, object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Removes a single blueprint version from the named catalog. Inverse of <see cref="PublishAsync" />.
    /// </summary>
    /// <param name="catalogName">Name of catalog</param>
    /// <param name="blueprintId">The blueprint id (name + version) to remove</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnpublishAsync(string catalogName, BlueprintId blueprintId, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    /// Removes all versions of a blueprint from the named catalog.
    /// </summary>
    /// <param name="catalogName">Name of catalog</param>
    /// <param name="blueprintName">The blueprint name (without version) to remove entirely</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnpublishAllVersionsAsync(string catalogName, string blueprintName, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    /// Returns true if the blueprint exists in any catalog
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <returns>True if the blueprint exists</returns>
    Task<bool> IsExistingAsync(BlueprintId blueprintId, object? sourceIdentifier = null);

    /// <summary>
    /// Returns true if the blueprint within the version range exists in any catalog
    /// </summary>
    /// <param name="blueprintIdVersionRange">The blueprint id with version range</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <returns>Result indicating if blueprint exists</returns>
    Task<BlueprintExistingResult> IsExistingAsync(BlueprintIdVersionRange blueprintIdVersionRange,
        object? sourceIdentifier = null);

    /// <summary>
    /// Refreshes the catalog cache for all catalogs
    /// </summary>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <param name="force">When true, forces every catalog to rebuild its cache unconditionally,
    ///     bypassing cache-TTL and unchanged-remote-timestamp short-circuits.</param>
    Task RefreshAllCatalogCachesAsync(object? sourceIdentifier = null, bool force = false);
}
