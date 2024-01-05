using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Interface for resolving references of construction kit elements.  E. g. derived types, records or cross references
/// </summary>
internal interface IReferenceResolver
{
    /// <summary>
    ///     Resolves and checks cross reference within the model graph.
    /// </summary>
    /// <param name="modelGraph"></param>
    /// <param name="operationResult"></param>
    void Resolve(CkModelGraph modelGraph,
        OperationResult operationResult);
}