using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     Interface for a CK model catalog
/// </summary>
public interface ICatalog
{
    /// <summary>
    ///     Returns the order of the catalog that will be used to resolve construction kit models.
    /// The lower the order, the higher the priority.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Returns the name of the catalog, used for identification. catalog names must be unique.
    /// </summary>
    string CatalogName { get; }

    /// <summary>
    ///     Returns the description of the catalog, used for outputs including configuration information.
    /// </summary>
    public string Description { get; }

    /// <summary>
    ///     Returns true if the catalog can be used to publish or update models, otherwise false.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Returns true if the catalog can be used to read models, otherwise false.
    /// </summary>
    bool CanRead { get; }

    /// <summary>
    /// Refreshes the catalog of the catalog, e.g., by reloading from disk or fetching from a remote source.
    /// </summary>
    /// <returns></returns>
    Task RefreshCatalogAsync();

    /// <summary>
    ///     Returns true, if the defined source identifier ist supported by the catalog.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <returns></returns>
    bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <summary>
    ///     Checks if a model exists in this catalog
    /// </summary>
    /// <param name="modelIdVersionRange">The construction kit model id with version range</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <returns>A result indicating if the model exists and if yes, which version</returns>
    Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange, object? sourceIdentifier = null);

    /// <summary>
    ///     Checks if a model exists in this catalog
    /// </summary>
    /// <param name="modelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <returns>True if the model exists in this catalog, otherwise false</returns>
    Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null);

    /// <summary>
    ///     Gets a model by its id
    /// </summary>
    /// <param name="modelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results that contain validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>The deserialized and schema validated construction kit model</returns>
    Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Publishes a model to the catalog
    /// </summary>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="force">Forces the operation by replacing model files if they exist.</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    /// Lists all CK models in the catalog for the defined source identifier.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    ///     set it to null to use default</param>
    /// <returns>List of CK models that are available in the catalog</returns>
    IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier);

    /// <summary>
    /// Searches for CK models in the catalog for the defined source identifier and search term.
    /// </summary>
    /// <param name="searchTerm">Search term to search for in the CK models</param>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <returns>List of CK models that match the search term</returns>
    IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier);
}