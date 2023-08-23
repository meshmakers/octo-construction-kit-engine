using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;

public interface IDependencyResolver
{
    Task<CkAggregatedModelElements> ResolveDependenciesAsync(ICollection<CkModelId> dependencies, OperationResult operationResult);
}