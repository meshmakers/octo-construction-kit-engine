using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
/// Manages the CK model repositories
/// </summary>
public interface ICkModelRepositoryService
{
    /// <summary>
    /// Looks up a model by its id 
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results that contains validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult, object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    /// Returns a list of known construction kit model repositories
    /// </summary>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <returns></returns>
    IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null);

    /// <summary>
    /// Publishes a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">Deserialized construction kit model.</param>
    /// <param name="isForced">When true, existing construction kit models are replaced.</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced, object? sourceIdentifier = null, CancellationToken? cancellationToken = null);
    
    /// <summary>
    /// Updates a model to a repository
    /// </summary>
    /// <param name="repositoryName">Name of Repository.</param>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="sourceIdentifier">An object that describes the source which the repository should search, set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null, CancellationToken? cancellationToken = null);
}