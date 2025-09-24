using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving the content of a compiled model including resolving dependencies.
/// </summary>
public interface IModelResolver
{
    /// <summary>
    ///     Resolves a list of model ids
    /// </summary>
    /// <param name="ckModelIds">List of model ids</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns></returns>
    Task<CkModelGraph> ResolveAsync(ICollection<CkModelId> ckModelIds, OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    ///     Resolves a list of model ids
    /// </summary>
    /// <param name="ckModelIds">List of model ids</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns></returns>
    Task<CkModelGraph> ResolveAsync(ICollection<CkModelId> ckModelIds, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    ///     Loads the compiled model into the resolver.
    /// </summary>
    /// <param name="compiledModel">Instance of the compiled model</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    Task<CkModelGraph> ResolveAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver, 
        OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    ///     Loads the compiled model into the resolver.
    /// </summary>
    /// <param name="compileCandidate">Instance of the compile candidate</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    Task<(CkModelGraph, CkCompiledModelRoot)> ResolveAsync(CkModelCompileCandidate compileCandidate, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null);
}