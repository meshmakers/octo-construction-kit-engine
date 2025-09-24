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
    public async Task<IReadOnlyCollection<CkModelId>> ResolveDependenciesAsync(ICollection<CkModelIdVersionRange> dependencyVersionRanges, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        logger.LogDebug("Starting resolving dependencies");
        var resolvedModelIds = await Resolve(dependencyVersionRanges, ckModelGraph, variableResolver, originFileResolver, sourceIdentifier,
                operationResult)
            .ConfigureAwait(false);
        logger.LogDebug("Resolving dependencies completed");

        return resolvedModelIds;
    }

    public async Task ResolveDependenciesAsync(ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph, IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        logger.LogDebug("Starting resolving dependencies");
        await Resolve(dependencies.Select(d=>d.ToVersionRange()).ToList(), ckModelGraph, variableResolver, originFileResolver, sourceIdentifier,
                operationResult)
            .ConfigureAwait(false);
        logger.LogDebug("Resolving dependencies completed");
    }

    private async Task<IReadOnlyCollection<CkModelId>> Resolve(ICollection<CkModelIdVersionRange> ckRootDependencies, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver,
        object? sourceIdentifier, OperationResult operationResult)
    {
        List<CkModelIdVersionRange> dependencies = [..ckRootDependencies];
        List<CkModelId> resolvedRootDependencies = [];

        for (var i = 0; i < dependencies.Count; i++)
        {
            var ckDependency = dependencies[i];

            logger.LogInformation("Resolving dependency '{CkModelId}'", ckDependency);

            var modelExistingResult = await ckModelRepositoryManagerLazy.Value.IsCkModelExistingAsync(ckDependency, sourceIdentifier)
                .ConfigureAwait(false);

            if (ckRootDependencies.Contains(ckDependency) && modelExistingResult is { Exists: true, ModelId: not null })
            {
                resolvedRootDependencies.Add(modelExistingResult.ModelId);
            }

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
                    var childDependencyRange = ckChildDependency.ToVersionRange();
                    if (!ckModelGraph.Dependencies.ContainsKey(ckChildDependency) && !dependencies.Contains(childDependencyRange))
                    {
                        logger.LogDebug("Adding additional dependency '{CkTypeId}'", ckChildDependency);
                        dependencies.Add(childDependencyRange);
                    }
                }
            }

            logger.LogDebug("Adding resolved dependency '{CkModelId}' to dependency graph",
                ckDependencyRootModel.ModelId);
            ckModelGraph.AppendModel(ckDependencyRootModel);
        }

        return resolvedRootDependencies.AsReadOnly();
    }
}