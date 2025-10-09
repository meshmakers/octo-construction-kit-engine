using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;

/// <summary>
///     Interface for resolving the content of a compiled model including resolving dependencies.
/// </summary>
public interface ICatalogModelResolver : IModelResolver
{
    /// <summary>
    ///     Loads the compiled model into the resolver.
    /// </summary>
    /// <param name="compileCandidate">Instance of the compile candidate</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    Task<(CkModelGraph, CkCompiledModelRoot)> CompileAsync(CkModelCompileCandidate compileCandidate,
        IOriginFileResolver originFileResolver,
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
}