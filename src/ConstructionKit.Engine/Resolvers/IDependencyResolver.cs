using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving construction kit dependencies
/// </summary>
internal interface IDependencyResolver
{
    /// <summary>
    ///     Resolves the dependencies based on the given version ranges
    /// </summary>
    /// <param name="dependencyVersionRanges">Dependencies to resolve</param>
    /// <param name="ckModelGraph">The model graph to add the resolved dependencies to</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns></returns>
    Task<IReadOnlyCollection<CkModelId>> ResolveDependenciesAsync(ICollection<CkModelIdVersionRange> dependencyVersionRanges, CkModelGraph ckModelGraph, IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    /// Resolves the dependencies of the given construction kit model ids
    /// </summary>
    /// <param name="dependencies">Dependencies to resolve</param>
    /// <param name="ckModelGraph">The model graph to add the resolved dependencies to</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns></returns>
    /// <returns></returns>
    Task ResolveDependenciesAsync(ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph, IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null);
}