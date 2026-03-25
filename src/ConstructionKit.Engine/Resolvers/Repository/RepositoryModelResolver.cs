using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;

/// <summary>
///     Resolver that resolves the elements of a compiled model.
/// </summary>
internal class RepositoryModelResolver : ModelResolver, IRepositoryModelResolver
{
    private readonly IRepositoryDependencyResolver _repositoryDependencyResolver;

    /// <summary>
    ///     Creates a new instance of <see cref="ModelResolver" />.
    /// </summary>
    /// <param name="repositoryDependencyResolver"></param>
    /// <param name="inheritanceResolver"></param>
    /// <param name="elementResolver"></param>
    /// <param name="referenceResolver"></param>
    /// <param name="variableResolver"></param>
    public RepositoryModelResolver(
        IRepositoryDependencyResolver repositoryDependencyResolver,
        IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver, IReferenceResolver referenceResolver,
        IVariableResolver variableResolver) : base(inheritanceResolver, elementResolver, referenceResolver,
        variableResolver)
    {
        _repositoryDependencyResolver = repositoryDependencyResolver;
    }

    public async Task<CkModelGraph> HardResolveAsync(ICollection<CkModelId> ckModelIds,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();
        await _repositoryDependencyResolver.HardResolveDependenciesAsync(
                ckModelIds.Select(id => id.ToVersionRange()).ToList(),
                modelGraph, _variableResolver,
                originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);

        _referenceResolver.Resolve(modelGraph, originFileResolver, operationResult);
        _inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }

    public async Task<ModelResolveResult> SoftResolveAsync(ICollection<CkModelId> ckModelIds,
        IOriginFileResolver originFileResolver, OperationResult operationResult,
        object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();

        var dependencyResolveResult = await _repositoryDependencyResolver.SoftResolveDependenciesAsync(ckModelIds,
                modelGraph, _variableResolver,
                originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);

        _referenceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        var failedModelIds = new HashSet<CkModelId>();
        _inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult, failedModelIds);

        return new ModelResolveResult
        {
            CkModelGraph = modelGraph,
            SkippedModelIds = dependencyResolveResult.SkippedModelIds,
            UnresolvedDependencyModelIds = dependencyResolveResult.UnresolvedDependencyModelIds,
            FailedModelIds = failedModelIds
        };
    }

    public async Task<ModelResolveResult> SoftResolveAsync(CkCompiledModelRoot compiledModel,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();

        DependencyResolveResult? dependencyResolveResult = null;
        if (compiledModel.Dependencies != null)
        {
            dependencyResolveResult = await _repositoryDependencyResolver.SoftResolveDependenciesAsync(
                    compiledModel.Dependencies, modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        Resolve(compiledModel, modelGraph, originFileResolver, operationResult);
        return new ModelResolveResult
        {
            CkModelGraph = modelGraph,
            SkippedModelIds = dependencyResolveResult?.SkippedModelIds ?? [],
            UnresolvedDependencyModelIds = dependencyResolveResult?.UnresolvedDependencyModelIds ?? [],
            FailedModelIds = []
        };
    }

    public async Task<CkModelGraph> HardResolveAsync(CkCompiledModelRoot compiledModel,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();

        if (compiledModel.Dependencies != null)
        {
            await _repositoryDependencyResolver.HardResolveDependenciesAsync(
                    compiledModel.Dependencies, modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        Resolve(compiledModel, modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }
}