using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

internal class DependencyResolver : IDependencyResolver
{
    private readonly ILogger<DependencyResolver> _logger;
    private readonly Lazy<ICkModelRepositoryManager> _ckModelRepositoryManagerLazy;

    public DependencyResolver(ILogger<DependencyResolver> logger, Lazy<ICkModelRepositoryManager> ckModelRepositoryManagerLazy)
    {
        _logger = logger;
        _ckModelRepositoryManagerLazy = ckModelRepositoryManagerLazy;
    }

    public async Task<CkModelGraph> ResolveDependenciesAsync(ICollection<CkModelId> dependencies, CkModelGraph ckModelGraph, OperationResult operationResult)
    {
        _logger.LogInformation("Starting resolving dependencies");
        await Resolve(dependencies, ckModelGraph, operationResult).ConfigureAwait(false);
        _logger.LogInformation("Resolving dependencies completed");

        return ckModelGraph;
    }

    private async Task Resolve(ICollection<CkModelId> ckRootDependencies, CkModelGraph ckModelGraph, OperationResult operationResult)
    {
        List<CkModelId> dependencies = new(ckRootDependencies);

        for (int i = 0; i < dependencies.Count; i++)
        {
            var ckDependency = dependencies[i];
            
            _logger.LogInformation("Resolving dependency '{CkTypeId}'", ckDependency);
            var ckDependencyRootModel = await _ckModelRepositoryManagerLazy.Value.LookupCkModelAsync(ckDependency, operationResult).ConfigureAwait(false);
            if (ckDependencyRootModel == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(ckDependency));
                continue;
            }
            
            if (ckDependencyRootModel.Dependencies != null)
            {
                foreach (var ckChildDependency in ckDependencyRootModel.Dependencies)      
                {
                    if (!ckModelGraph.Dependencies.ContainsKey(ckChildDependency))
                    {
                        _logger.LogInformation("Adding additional dependency '{CkTypeId}'", ckChildDependency);
                        dependencies.Add(ckChildDependency);
                    }
                }
            }
            
            _logger.LogInformation("Adding resolved dependency '{CkTypeId}' to dependency graph", ckDependencyRootModel.ModelId);
            ckModelGraph.AppendModel(ckDependencyRootModel);
        }
    }
}