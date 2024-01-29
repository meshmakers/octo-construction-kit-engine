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
    /// <param name="ckCompiledModelRoot">The construction kit model root to resolve</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="operationResult">Operation result</param>
    /// <returns></returns>
    CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, IVariableResolver variableResolver, OperationResult operationResult);
}