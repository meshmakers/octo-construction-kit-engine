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
    /// Gets the absolute path to a blueprint's directory
    /// </summary>
    /// <param name="blueprintId">The blueprint id</param>
    /// <param name="sourceIdentifier">Source identifier, null for default</param>
    /// <returns>The path to the blueprint directory</returns>
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
    Task RefreshAllCatalogCachesAsync(object? sourceIdentifier = null);
}
