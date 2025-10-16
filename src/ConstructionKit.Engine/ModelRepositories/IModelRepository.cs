using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Represents a repository that can store and retrieve construction kit models
/// </summary>
public interface IModelRepository
{
    /// <summary>
    /// Returns true if the model with the given id and version exists
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>The task that returns true if the model exists</returns>
    Task<bool> IsExistingAsync(CkModelId ckModelId, object? sourceIdentifier = null);

    /// <summary>
    /// Returns information if the model with the given id and version range exists
    /// </summary>
    /// <param name="ckModelIdVersionRange">The construction kit model id with optional version range</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <returns>The task that returns an object describing if the model exists</returns>
    Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null);

    /// <summary>
    ///     Customizes CkEnum values in the repository
    /// </summary>
    /// <param name="ckEnumId">Construction kit enum id</param>
    /// <param name="ckEnumUpdates">Describes the updates to the enum</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task CustomizeCkEnumAsync(CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Updates a model to a repository
    /// </summary>
    /// <param name="ckCompiledModel">The validated construction kit model</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the catalog should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns></returns>
    Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <summary>
    ///     Tries to look up a model by its id
    /// </summary>
    /// <param name="ckModelId">The construction kit model id</param>
    /// <param name="operationResult">Operation results
    /// that contain validation messages occured during deserialization.</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
    /// <returns>If existing the deserialized and validated construction kit model</returns>
    public Task<CkCompiledModelRoot?> TryLookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);
}