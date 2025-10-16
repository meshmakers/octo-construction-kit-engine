using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;

internal class RepositoryDependencyResolver(
    ILogger<RepositoryDependencyResolver> logger,
    Lazy<IRepositoryManagementService> ckRepositoryManagementService)
    : IRepositoryDependencyResolver
{
    public async Task<IReadOnlyCollection<CkModelId>> HardResolveDependenciesAsync(
        ICollection<CkModelIdVersionRange> dependencyVersionRanges, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        logger.LogDebug("Starting resolving dependencies");
        var resolveResult = await Resolve(dependencyVersionRanges, ckModelGraph, variableResolver, originFileResolver,
                sourceIdentifier,
                operationResult)
            .ConfigureAwait(false);
        logger.LogDebug("Resolving dependencies completed");

        if (resolveResult.UnresolvedDependencyModelIds.Any())
        {
            foreach (var ckDependency in resolveResult.UnresolvedDependencyModelIds)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckDependency),
                    ckDependency));
            }
        }

        return resolveResult.RootDependencyModelIds;
    }

    public Task<IReadOnlyCollection<CkModelId>> HardResolveDependenciesAsync(ICollection<CkModelId> dependencies,
        CkModelGraph ckModelGraph,
        IVariableResolver variableResolver, IOriginFileResolver originFileResolver, OperationResult operationResult,
        object? sourceIdentifier = null)
    {
        return HardResolveDependenciesAsync(dependencies.Select(x => x.ToVersionRange()).ToList(), ckModelGraph,
            variableResolver, originFileResolver, operationResult, sourceIdentifier);
    }

    public async Task<DependencyResolveResult> SoftResolveDependenciesAsync(ICollection<CkModelId> dependencies,
        CkModelGraph ckModelGraph, IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        logger.LogDebug("Starting resolving dependencies");
        var resolveResult = await Resolve(dependencies.Select(d => d.ToVersionRange()).ToList(), ckModelGraph,
                variableResolver, originFileResolver, sourceIdentifier,
                operationResult)
            .ConfigureAwait(false);
        logger.LogDebug("Resolving dependencies completed");

        return resolveResult;
    }

    private async Task<DependencyResolveResult> Resolve(ICollection<CkModelIdVersionRange> ckRootDependencies,
        CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver,
        object? sourceIdentifier, OperationResult operationResult)
    {
        List<Tuple<List<CkModelId>, CkModelIdVersionRange>> dependencies =
        [
            ..ckRootDependencies.Select(range =>
                Tuple.Create(new List<CkModelId>(), range))
        ];
        // List of already resolved dependencies to avoid circular references
        List<CkModelId> resolvedDependencies = [];

        // List of dependencies that were skipped because their dependencies could not be resolved
        List<CkModelId> skippedDependencies = [];

        // List of root dependencies that were resolved
        List<CkModelId> resolvedRootDependencies = [];

        // List of dependencies that could not be resolved
        List<CkModelIdVersionRange> unresolvedDependencies = [];

        List<CkCompiledModelRoot> ckResolvedModels = [];

        for (var i = 0; i < dependencies.Count; i++)
        {
            var ckOriginModelIds = dependencies[i].Item1;
            var ckDependency = dependencies[i].Item2;

            logger.LogInformation("Resolving dependency '{CkModelId}'", ckDependency);

            var modelExistingResult = await ckRepositoryManagementService.Value
                .IsExistingAsync(ckDependency, sourceIdentifier)
                .ConfigureAwait(false);
            logger.LogInformation("Resolving dependency '{CkModelId}' found: '{Exists}'", ckDependency, modelExistingResult.Exists);

            // General dependency was already resolved
            if (modelExistingResult is { Exists: true, ModelId: not null } && !resolvedDependencies.Contains(modelExistingResult.ModelId))
            {
                resolvedDependencies.Add(modelExistingResult.ModelId);
            }

            // Dependency not resolved because it does not exist
            if (!modelExistingResult.Exists || modelExistingResult.ModelId == null)
            {
                foreach (var ckOriginModelId in ckOriginModelIds)
                {
                    if (!skippedDependencies.Contains(ckOriginModelId))
                    {
                        skippedDependencies.Add(ckOriginModelId);
                    }
                }

                unresolvedDependencies.Add(ckDependency);
                continue;
            }

            var ckDependencyRootModel = await ckRepositoryManagementService.Value
                .TryLookupCkModelAsync(modelExistingResult.ModelId, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
            if (ckDependencyRootModel == null)
            {
                operationResult.AddMessage(MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckDependency),
                    ckDependency));

                continue;
            }

            variableResolver.SetVariable(ckDependencyRootModel.ModelId.Name, ckDependencyRootModel.ModelId.FullName);

            if (ckDependencyRootModel.Dependencies != null)
            {
                foreach (var ckChildDependency in ckDependencyRootModel.Dependencies)
                {
                    var childDependencyRange = ckChildDependency.ToVersionRange();
                    var childDependencyOrigins = dependencies.SingleOrDefault(d => d.Item2 == childDependencyRange)?.Item1;
                    if (childDependencyOrigins == null)
                    {
                        dependencies.Add(new Tuple<List<CkModelId>, CkModelIdVersionRange>([ckDependencyRootModel.ModelId], childDependencyRange));
                    }
                    else
                    {
                        childDependencyOrigins.Add(ckDependencyRootModel.ModelId);
                    }
                }
            }

            ckResolvedModels.Add(ckDependencyRootModel);
        }

        for (int i = 0; i < skippedDependencies.Count; i++)
        {
            var skippedDependency = skippedDependencies[i];

            var dependents = dependencies.Where(x => x.Item2.IsSatisfiedBy(skippedDependency.Name));
            foreach (var dependent in dependents)
            {
                foreach (var ckOriginModelId in dependent.Item1)
                {
                    if (!skippedDependencies.Contains(ckOriginModelId))
                    {
                        skippedDependencies.Add(ckOriginModelId);
                    }
                }
            }
        }

        foreach (var ckCompiledModelRoot in ckResolvedModels)
        {
            if (skippedDependencies.Contains(ckCompiledModelRoot.ModelId))
            {
                continue;
            }

            logger.LogDebug("Adding resolved dependency '{CkModelId}' to dependency graph",
                ckCompiledModelRoot.ModelId);
            ckModelGraph.AppendModel(ckCompiledModelRoot);

            // This is a root dependency and was resolved
            if (ckRootDependencies.Any(x => x.IsSatisfiedBy(ckCompiledModelRoot.ModelId)))
            {
                resolvedRootDependencies.Add(ckCompiledModelRoot.ModelId);
            }
        }

        return new DependencyResolveResult
        {
            RootDependencyModelIds = resolvedRootDependencies.AsReadOnly(),
            ResolvedDependentModelIds = resolvedDependencies.AsReadOnly(),
            SkippedModelIds = skippedDependencies.AsReadOnly(),
            UnresolvedDependencyModelIds = unresolvedDependencies.AsReadOnly()
        };
    }
}