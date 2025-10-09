using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;

/// <summary>
/// Interface for resolving dependencies based on a catalog
/// </summary>
public interface ICatalogDependencyResolver
{
    /// <summary>
    /// Resolves the dependencies based on the given version ranges,
    /// this method will fail if a dependency cannot be resolved by adding according messages to the operation result.
    /// </summary>
    /// <param name="dependencyVersionRanges">Dependencies to resolve</param>
    /// <param name="ckModelGraph">The model graph to add the resolved dependencies to</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>A list of resolved model ids</returns>
    Task<IReadOnlyCollection<CkModelId>> HardResolveDependenciesAsync(
        ICollection<CkModelIdVersionRange> dependencyVersionRanges, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null);

    /// <summary>
    /// Resolves the dependencies based on the given version ranges,
    /// this method will fail if a dependency cannot be resolved by adding according messages to the operation result.
    /// </summary>
    /// <param name="dependencies">Dependencies to resolve</param>
    /// <param name="ckModelGraph">The model graph to add the resolved dependencies to</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    /// <param name="sourceIdentifier">An object
    /// that describes the source
    /// which the repository should search set it to null to use default</param>
    /// <returns>A list of resolved model ids</returns>
    Task<IReadOnlyCollection<CkModelId>> HardResolveDependenciesAsync(
        ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null);

}