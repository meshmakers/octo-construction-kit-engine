using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

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
    /// <param name="ckModelGraph">The model graph to add the resolved elements to</param>
    /// <param name="variableResolver">Service for resolving variables</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    void Resolve(CkCompiledModelRoot ckCompiledModelRoot, CkModelGraph ckModelGraph, IVariableResolver variableResolver, IOriginFileResolver originFileResolver,
        OperationResult operationResult);
}