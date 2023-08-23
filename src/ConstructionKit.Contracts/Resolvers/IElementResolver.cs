using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;

public interface IElementResolver
{
    CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, OperationResult validationResult);
}