using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

internal class DependencyResolver(
    ILogger<DependencyResolver> logger,
    Lazy<ICkModelRepositoryManager> ckModelRepositoryManagerLazy)
    : IDependencyResolver
{
    public async Task ResolveDependenciesAsync(ICollection<CkModelIdVersionRange> dependencies, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        logger.LogDebug("Starting resolving dependencies");
        await Resolve(dependencies, ckModelGraph, variableResolver, originFileResolver, sourceIdentifier,
                operationResult)
            .ConfigureAwait(false);
        logger.LogDebug("Resolving dependencies completed");
    }

    private async Task Resolve(ICollection<CkModelIdVersionRange> ckRootDependencies, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver,
        object? sourceIdentifier, OperationResult operationResult)
    {
        List<CkModelIdVersionRange> dependencies = [..ckRootDependencies];

        for (var i = 0; i < dependencies.Count; i++)
        {
            var ckDependency = dependencies[i];

            logger.LogDebug("Resolving dependency '{CkModelId}'", ckDependency);

            var modelExistingResult = await ckModelRepositoryManagerLazy.Value.IsCkModelExistingAsync(ckDependency, sourceIdentifier)
                .ConfigureAwait(false);
            if (!modelExistingResult.Exists || modelExistingResult.ModelId == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckDependency),
                    ckDependency));
                continue;
            }

            var ckDependencyRootModel = await ckModelRepositoryManagerLazy.Value
                .LookupCkModelAsync(modelExistingResult.ModelId, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
            if (ckDependencyRootModel == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckDependency),
                    ckDependency));
                continue;
            }

            variableResolver.SetVariable(ckDependencyRootModel.ModelId.ModelId, ckDependencyRootModel.ModelId.FullName);

            if (ckDependencyRootModel.Dependencies != null)
            {
                foreach (var ckChildDependency in ckDependencyRootModel.Dependencies)
                {
                    if (ckModelGraph.Dependencies.Keys.Any(t=> ckChildDependency.IsSatisfiedBy(t)) &&
                        !dependencies.Contains(ckChildDependency))
                    {
                        logger.LogDebug("Adding additional dependency '{CkTypeId}'", ckChildDependency);
                        dependencies.Add(ckChildDependency);
                    }
                }
            }

            logger.LogDebug("Adding resolved dependency '{CkModelId}' to dependency graph",
                ckDependencyRootModel.ModelId);
            ckModelGraph.AppendModel(ckDependencyRootModel);
        }
    }
}