using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Interface for resolving construction kit elements
/// </summary>
public interface IElementResolver
{
    /// <summary>
    /// Resolves the given construction kit model root
    /// </summary>
    /// <param name="ckCompiledModelRoot"></param>
    /// <param name="validationResult"></param>
    /// <returns></returns>
    CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, OperationResult validationResult);
}