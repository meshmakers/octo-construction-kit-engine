using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving construction kit elements
/// </summary>
internal interface IElementResolver
{
    /// <summary>
    ///     Resolves the given construction kit model root
    /// </summary>
    /// <param name="ckCompiledModelRoot"></param>
    /// <param name="operationResult"></param>
    /// <returns></returns>
    CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, OperationResult operationResult);
}