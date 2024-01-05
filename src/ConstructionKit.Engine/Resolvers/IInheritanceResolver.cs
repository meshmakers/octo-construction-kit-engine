using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Resolves inheritance of ck types
/// </summary>
internal interface IInheritanceResolver
{
    /// <summary>
    ///     Resolves the inheritance of the given model elements
    /// </summary>
    /// <param name="modelGraph"></param>
    /// <param name="operationResult"></param>
    /// <returns></returns>
    CkModelGraph Resolve(CkModelGraph modelGraph, OperationResult operationResult);
}