using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;

/// <summary>
/// Resolves inheritance of ck types
/// </summary>
public interface IInheritanceResolver
{
    /// <summary>
    /// Resolves the inheritance of the given model elements
    /// </summary>
    /// <param name="aggregatedModelElements"></param>
    /// <param name="modelGraph"></param>
    /// <param name="operationResult"></param>
    /// <returns></returns>
    CkModelGraph Resolve(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph, OperationResult operationResult);
}