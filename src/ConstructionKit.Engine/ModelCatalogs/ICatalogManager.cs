using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     Manages construction kit catalogs
/// </summary>
internal interface ICatalogManager
{
    /// <summary>
    /// Searches for models in all known catalogs
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <param name="skip">Amount of models to skip</param>
    /// <param name="take">Amount of models to take</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    /// <returns>A search result containing the models found</returns>
    Task<ModelSearchResult> SearchAsync(string searchTerm, int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Searches for models in a specific catalog
    /// </summary>
    /// <param name="catalogName">catalog name to search models from</param>
    /// <param name="searchTerm">Search term</param>
    /// <param name="skip">Amount of models to skip</param>
    /// <param name="take">Amount of models to take</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    /// <returns>A search result containing the models found</returns>
    Task<ModelSearchResult> SearchAsync(string catalogName, string searchTerm, int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Lists models in all known catalogs.
    /// A unique list of models is returned, so if a model exists in multiple catalogs
    /// </summary>
    /// <param name="skip">Amount of models to skip</param>
    /// <param name="take">Amount of models to take</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    /// <returns>A list result containing the models found</returns>
    Task<ModelListResult> ListAsync(int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Lists models in a specific catalog
    /// </summary>
    /// <param name="catalogName">catalog name to list models from</param>
    /// <param name="skip">Amount of models to skip</param>
    /// <param name="take">Amount of models to take</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    /// <returns>A list result containing the models found</returns>
    Task<ModelListResult> ListAsync(string catalogName, int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);


    /// <summary>
    ///     Tries to look up a model by its id in all known catalogs
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> TryGetAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Tries to look up a model by its id in a specific catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> TryGetAsync(string catalogName, CkModelId ckModelId, OperationResult operationResult,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Looks up a model by its id
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot> GetAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Looks up a model by its id in a specific catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot> GetAsync(string catalogName, CkModelId ckModelId, OperationResult operationResult,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns a list of known construction kit model catalogs
    /// </summary>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>Returns a tuple with name and description of catalog</returns>
    IEnumerable<Tuple<string, string>> GetCatalogList(object? sourceIdentifier = null);

    /// <summary>
    ///     Publishes a model to a catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="ckCompiledModel">Deserialized construction kit model.</param>
    /// <param name="isForced">When true, existing construction kit models are replaced.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishAsync(string catalogName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns true if the model exists in a given catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given catalog</returns>
    Task<bool> IsExistingAsync(string catalogName, CkModelId ckModelId, object? sourceIdentifier = null);

    /// <summary>
    /// Returns true if the model exists in a given catalog
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns></returns>
    Task<bool> IsExistingAsync(CkModelId ckModelId, object? sourceIdentifier = null);

    /// <summary>
    ///     Returns true if the model within the version range exists in a given catalog
    /// </summary>
    /// <param name="ckModelIdVersionRange">The construction kit model id with version range</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given catalog</returns>
    Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange ckModelIdVersionRange, object? sourceIdentifier = null);

    /// <summary>
    ///     Returns true if the model within the version range exists in a given catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="ckModelIdVersionRange">The construction kit model id with version range</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given catalog</returns>
    Task<ModelExistingResult> IsExistingAsync(string catalogName, CkModelIdVersionRange ckModelIdVersionRange, object? sourceIdentifier = null);

    /// <summary>
    /// Refreshes the catalog cache for a specific catalog
    /// </summary>
    /// <param name="catalogName">Name of catalog.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns></returns>
    Task RefreshCatalogCacheAsync(string catalogName, object? sourceIdentifier = null);

    /// <summary>
    /// Refreshes the catalog cache for all catalogs
    /// </summary>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns></returns>
    Task RefreshAllCatalogCachesAsync(object? sourceIdentifier = null);
}