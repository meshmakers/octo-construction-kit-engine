using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving references of construction kit elements.  E. g. derived types, records or cross references
/// </summary>
internal interface IReferenceResolver
{
    /// <summary>
    ///     Resolves and checks cross reference within the model graph.
    /// </summary>
    /// <param name="modelGraph">Model graph to resolve</param>
    /// <param name="originFileResolver">Resolver for the original file location</param>
    /// <param name="operationResult">Operation result</param>
    void Resolve(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult);
}