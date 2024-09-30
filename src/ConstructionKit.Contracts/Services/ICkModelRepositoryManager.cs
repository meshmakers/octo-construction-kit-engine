using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Manages the CK model repositories
/// </summary>
public interface ICkModelRepositoryService
{
    /// <summary>
    ///     Looks up a model by its id
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results that contains validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns a list of known construction kit model repositories
    /// </summary>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <returns>Returns a tuple with name and description of repository</returns>
    IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null);

    /// <summary>
    ///     Publishes a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">Deserialized construction kit model.</param>
    /// <param name="isForced">When true, existing construction kit models are replaced.</param>
    /// <param name="publishExtensions">When true, custom extensions are published, e.g. custom enum values</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        bool publishExtensions, object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Updates a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="publishExtensions">When true, custom extensions are published, e.g. custom enum values</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool publishExtensions, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);
    
    /// <summary>
    ///     Customizes CkEnum values in the repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckEnumId">Construction kit enum id</param>
    /// <param name="ckEnumUpdates">Describes the updates to the enum</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task CustomizeCkEnumAsync(string repositoryName, CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns true if the model exists in given repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in given repository</returns>
    Task<bool> IsCkModelExistingAsync(string repositoryName, CkModelId ckModelId, object? sourceIdentifier = null);

    /// <summary>
    /// Restores construction kit models based on a construction kit model configuration file.
    /// </summary>
    /// <param name="modelConfigurationFilePath">Local file path where the model configuration file exists.</param>
    /// <param name="outputPath">Output path of compiled construction kit</param>
    /// <param name="createCacheFilePath">
    ///     When defined, a cache file is created at the defined path containing all
    ///     dependencies
    /// </param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <returns></returns>
    Task<IEnumerable<CompileResult>> RestoreConstructionKitModelsAsync(string modelConfigurationFilePath,
        string outputPath, string? createCacheFilePath, object? sourceIdentifier = null);

    /// <summary>
    /// Returns information about the construction kit model folder.
    /// </summary>
    /// <param name="modelConfigurationFilePath">Local file path where the model configuration file exists.</param>
    /// <param name="outputPath">Output path of compiled construction kit</param>
    /// <param name="createCacheFilePath">
    ///     When defined, a cache file is created at the defined path containing all
    ///     dependencies
    /// </param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <returns></returns>
    Task<IEnumerable<CompileResult>> RestoreConstructionKitModelsAsync(string modelConfigurationFilePath,
        string outputPath, string? createCacheFilePath, OperationResult operationResult,
        object? sourceIdentifier = null);
}