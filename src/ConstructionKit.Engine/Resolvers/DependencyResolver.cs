using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

internal class DependencyResolver : IDependencyResolver
{
    private readonly ILogger<DependencyResolver> _logger;
    private readonly ICkModelRepositoryManager _ckModelRepositoryManager;

    public DependencyResolver(ILogger<DependencyResolver> logger, ICkModelRepositoryManager ckModelRepositoryManager)
    {
        _logger = logger;
        _ckModelRepositoryManager = ckModelRepositoryManager;
    }

    public async Task<CkAggregatedModelElements> ResolveDependenciesAsync(ICollection<CkModelId> dependencies, OperationResult operationResult)
    {
        CkAggregatedModelElements aggregatedModelElements = new();

        _logger.LogInformation("Starting resolving dependencies");
        await Resolve(dependencies, aggregatedModelElements, operationResult);

        return aggregatedModelElements;
    }

    private async Task Resolve(ICollection<CkModelId> ckRootDependencies, CkAggregatedModelElements aggregatedModelElements, OperationResult operationResult)
    {
        List<CkModelId> dependencies = new(ckRootDependencies);

        for (int i = 0; i < dependencies.Count; i++)
        {
            var ckDependency = dependencies[i];
            
            _logger.LogInformation("Resolving dependency '{CkTypeId}'", ckDependency);
            var ckDependencyRootModel = await _ckModelRepositoryManager.LookupCkModelAsync(ckDependency);
            if (ckDependencyRootModel == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(ckDependency));
                continue;
            }
            
            if (ckDependencyRootModel.Dependencies != null)
            {
                foreach (var ckChildDependency in ckDependencyRootModel.Dependencies)      
                {
                    if (!aggregatedModelElements.CkModelDependencies.ContainsKey(ckChildDependency))
                    {
                        _logger.LogInformation("Adding additional dependency '{CkTypeId}'", ckChildDependency);
                        dependencies.Add(ckChildDependency);
                    }
                }
            }
            
            _logger.LogInformation("Adding resolved dependency '{CkTypeId}' to dependency graph", ckDependencyRootModel.ModelId);
            aggregatedModelElements.AppendModel(ckDependencyRootModel);
        }
    }
}