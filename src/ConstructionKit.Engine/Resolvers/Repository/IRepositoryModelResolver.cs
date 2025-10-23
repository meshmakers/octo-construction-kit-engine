using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;

/// <summary>
///     Interface for resolving the content of a compiled model including resolving dependencies.
/// </summary>
public interface IRepositoryModelResolver :IModelResolver
{
    /// <summary>
    /// Resolves a list of model ids,
    /// all dependencies must be available otherwise an error is reported in the operation result.
    /// </summary>
    /// <param name="ckModelIds">List of model ids</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>A resolved model graph</returns>
    Task<CkModelGraph> HardResolveAsync(ICollection<CkModelId> ckModelIds, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    ///     Loads the compiled model into the resolver. This method tries to resolve as much as possible and reports unresolved references in the result.
    /// </summary>
    /// <param name="compiledModel">Instance of the compiled model</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>Object that contains the resolved model graph</returns>
    Task<CkModelGraph> HardResolveAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    /// Loads the compiled model into the resolver. This method tries to resolve as much as possible and reports unresolved references in the result.
    /// </summary>
    /// <param name="ckModelIds">List of model ids to resolve</param>
    /// <param name="originFileResolver">File origin resolver</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">Source identifier</param>
    /// <returns>A result object containing the list of resolved model ids and the list of model ids that could not be resolved</returns>
    Task<ModelResolveResult> SoftResolveAsync(ICollection<CkModelId> ckModelIds,
        IOriginFileResolver originFileResolver, OperationResult operationResult,
        object? sourceIdentifier = null);

    /// <summary>
    /// Loads the compiled model into the resolver. This method tries to resolve as much as possible and reports unresolved references in the result.
    /// </summary>
    /// <param name="compiledModel">Compiled model to resolve</param>
    /// <param name="originFileResolver">File origin resolver</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">Source identifier</param>
    /// <returns>A result object containing the list of resolved model ids and the list of model ids that could not be resolved</returns>
    Task<ModelResolveResult> SoftResolveAsync(CkCompiledModelRoot compiledModel,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null);
}