using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

internal class DependencyResolver : IDependencyResolver
{
    private readonly Lazy<ICkModelRepositoryManager> _ckModelRepositoryManagerLazy;
    private readonly ILogger<DependencyResolver> _logger;

    public DependencyResolver(ILogger<DependencyResolver> logger, Lazy<ICkModelRepositoryManager> ckModelRepositoryManagerLazy)
    {
        _logger = logger;
        _ckModelRepositoryManagerLazy = ckModelRepositoryManagerLazy;
    }

    public async Task<CkModelGraph> ResolveDependenciesAsync(ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        _logger.LogDebug("Starting resolving dependencies");
        await Resolve(dependencies, ckModelGraph, originFileResolver, sourceIdentifier, operationResult).ConfigureAwait(false);
        _logger.LogDebug("Resolving dependencies completed");

        return ckModelGraph;
    }

    private async Task Resolve(ICollection<CkModelId> ckRootDependencies, CkModelGraph ckModelGraph, IOriginFileResolver originFileResolver, 
        object? sourceIdentifier, OperationResult operationResult)
    {
        List<CkModelId> dependencies = [..ckRootDependencies];

        for (var i = 0; i < dependencies.Count; i++)
        {
            var ckDependency = dependencies[i];

            _logger.LogDebug("Resolving dependency '{CkModelId}'", ckDependency);
            var ckDependencyRootModel = await _ckModelRepositoryManagerLazy.Value.LookupCkModelAsync(ckDependency, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
            if (ckDependencyRootModel == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckDependency), ckDependency));
                continue;
            }

            if (ckDependencyRootModel.Dependencies != null)
            {
                foreach (var ckChildDependency in ckDependencyRootModel.Dependencies)
                {
                    if (!ckModelGraph.Dependencies.ContainsKey(ckChildDependency))
                    {
                        _logger.LogDebug("Adding additional dependency '{CkTypeId}'", ckChildDependency);
                        dependencies.Add(ckChildDependency);
                    }
                }
            }

            _logger.LogDebug("Adding resolved dependency '{CkTypeId}' to dependency graph", ckDependencyRootModel.ModelId);
            ckModelGraph.AppendModel(ckDependencyRootModel);
        }
    }
}