using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
///     Manages the CK model repositories
/// </summary>
internal interface ICkModelRepositoryManager
{
    /// <summary>
    ///     Looks up a model by its id
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);
    
    /// <summary>
    ///     Looks up a model by its id in a specific repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(string repositoryName, CkModelId ckModelId, OperationResult operationResult,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns a list of known construction kit model repositories
    /// </summary>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>Returns a tuple with name and description of repository</returns>
    IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null);

    /// <summary>
    ///     Publishes a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">Deserialized construction kit model.</param>
    /// <param name="isForced">When true, existing construction kit models are replaced.</param>
    /// <param name="publishExtensions">When true, custom extensions are published, e.g. custom enum values</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        bool publishExtensions, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Updates a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="publishExtensions">When true, custom extensions are published, e.g. custom enum values</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool publishExtensions,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Customizes CK enum values in a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckEnumId">Construction kit enum id</param>
    /// <param name="ckEnumUpdates">Describes the updates to the enum</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task CustomizeCkEnumAsync(string repositoryName, CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Returns true if the model exists in a given repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given repository</returns>
    Task<bool> IsCkModelExistingAsync(string repositoryName, CkModelId ckModelId, object? sourceIdentifier = null);

    /// <summary>
    ///     Returns true if the model within the version range exists in a given repository
    /// </summary>
    /// <param name="ckModelIdVersionRange">The construction kit model id with version range</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given repository</returns>
    Task<ModelExistingResult> IsCkModelExistingAsync(CkModelIdVersionRange ckModelIdVersionRange, object? sourceIdentifier = null);

    /// <summary>
    ///     Returns true if the model within the version range exists in a given repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckModelIdVersionRange">The construction kit model id with version range</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists in a given repository</returns>
    Task<ModelExistingResult> IsCkModelExistingAsync(string repositoryName, CkModelIdVersionRange ckModelIdVersionRange, object? sourceIdentifier = null);
}