
namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving the content of a compiled model including resolving dependencies.
/// </summary>
public interface IModelResolver
{
    // /// <summary>
    // /// Resolves a list of model ids,
    // /// all dependencies must be available otherwise an error is reported in the operation result.
    // /// </summary>
    // /// <param name="ckModelIds">List of model ids</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>A resolved model graph</returns>
    // Task<CkModelGraph> HardResolveAsync(ICollection<CkModelId> ckModelIds, OperationResult operationResult,
    //     object? sourceIdentifier = null);
    //
    // /// <summary>
    // /// Resolves a list of model ids,
    // /// all dependencies must be available otherwise an error is reported in the operation result.
    // /// </summary>
    // /// <param name="ckModelIds">List of model ids</param>
    // /// <param name="originFileResolver">Resolver for the original file location</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>A resolved model graph</returns>
    // Task<CkModelGraph> HardResolveAsync(ICollection<CkModelId> ckModelIds, IOriginFileResolver originFileResolver,
    //     OperationResult operationResult, object? sourceIdentifier = null);
    //
    // /// <summary>
    // /// Loads a list of model ids into the resolver.
    // /// This method tries to resolve as much as possible and reports unresolved references in the result.
    // /// </summary>
    // /// <param name="ckModelIds">List of model ids</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>Object that contains the resolved model graph and information about unresolved references</returns>
    // Task<ModelResolveResult> SoftResolveAsync(ICollection<CkModelId> ckModelIds, OperationResult operationResult,
    //     object? sourceIdentifier = null);
    //
    // /// <summary>
    // /// Loads a list of model ids into the resolver.
    // /// This method tries to resolve as much as possible and reports unresolved references in the result.
    // /// </summary>
    // /// <param name="ckModelIds">List of model ids</param>
    // /// <param name="originFileResolver">Resolver for the original file location</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>A resolved model graph</returns>
    // Task<ModelResolveResult> SoftResolveAsync(ICollection<CkModelId> ckModelIds, IOriginFileResolver originFileResolver,
    //     OperationResult operationResult, object? sourceIdentifier = null);
    //
    // /// <summary>
    // ///     Loads the compiled model into the resolver. This method tries to resolve as much as possible and reports unresolved references in the result.
    // /// </summary>
    // /// <param name="compiledModel">Instance of the compiled model</param>
    // /// <param name="originFileResolver">Resolver for the original file location</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>Object that contains the resolved model graph and information about unresolved references</returns>
    // Task<ModelResolveResult> SoftResolveAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver,
    //     OperationResult operationResult, object? sourceIdentifier = null);
    //
    // /// <summary>
    // /// Loads the compiled model into the resolver and resolves all dependencies. When a dependency is missing it is reported in the operation result.
    // /// </summary>
    // /// <param name="compiledModel">Instance of the compiled model</param>
    // /// <param name="originFileResolver">Resolver for the original file location</param>
    // /// <param name="operationResult">Operation result</param>
    // /// <param name="sourceIdentifier">An object
    // /// that describes the source
    // /// which the repository should search set it to null to use default</param>
    // /// <returns>Resolved model graph</returns>
    // Task<CkModelGraph> HardResolveAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver,
    //     OperationResult operationResult, object? sourceIdentifier = null);
}