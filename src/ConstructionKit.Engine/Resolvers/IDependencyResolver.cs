using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Interface for resolving construction kit dependencies
/// </summary>
public interface IDependencyResolver
{
    /// <summary>
    /// Resolves the dependencies of the given construction kit model ids
    /// </summary>
    /// <param name="dependencies"></param>
    /// <param name="ckModelGraph"></param>
    /// <param name="operationResult"></param>
    /// <returns></returns>
    Task<CkModelGraph> ResolveDependenciesAsync(ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph,  OperationResult operationResult);
}