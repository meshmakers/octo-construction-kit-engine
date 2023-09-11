using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Interface for resolving the content of a compiled model including resolving dependencies.
/// </summary>
public interface IModelResolver
{
    /// <summary>
    /// Loads the compiled model into the resolver.
    /// </summary>
    /// <param name="compiledModel"></param>
    /// <param name="operationResult"></param>
    Task<CkModelGraph> ResolveAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult);
}