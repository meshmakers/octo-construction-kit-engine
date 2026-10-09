using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;

/// <summary>
///     Resolver that resolves the elements of a compiled model.
/// </summary>
internal class RepositoryModelResolver : ModelResolver, IRepositoryModelResolver
{
    private readonly IRepositoryDependencyResolver _repositoryDependencyResolver;
    private readonly ICkJsonSerializer _ckJsonSerializer;

    /// <summary>
    ///     Creates a new instance of <see cref="ModelResolver" />.
    /// </summary>
    /// <param name="repositoryDependencyResolver"></param>
    /// <param name="inheritanceResolver"></param>
    /// <param name="elementResolver"></param>
    /// <param name="referenceResolver"></param>
    /// <param name="variableResolver"></param>
    /// <param name="ckJsonSerializer">Used to copy a model before binding its references (AB#5665)</param>
    public RepositoryModelResolver(
        IRepositoryDependencyResolver repositoryDependencyResolver,
        IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver, IReferenceResolver referenceResolver,
        IVariableResolver variableResolver, ICkJsonSerializer ckJsonSerializer) : base(inheritanceResolver,
        elementResolver, referenceResolver, variableResolver)
    {
        _repositoryDependencyResolver = repositoryDependencyResolver;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <summary>
    ///     AB#5665: binds major-qualified references of the model to the resolved versions on a copy, so the
    ///     caller's instance (e.g. the model about to be persisted by an import) keeps its version-less form.
    /// </summary>
    private async Task<CkCompiledModelRoot> BindForResolveAsync(CkCompiledModelRoot compiledModel,
        CkModelGraph modelGraph)
    {
        if (!CkReferenceRewriter.HasMajorQualifiedReferences(compiledModel))
        {
            return compiledModel;
        }

        var copy = await CkCompiledModelCloner.CloneAsync(_ckJsonSerializer, compiledModel).ConfigureAwait(false);
        CkReferenceRewriter.BindMajorQualified(copy, modelGraph.Models.Keys);
        return copy;
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
        var dependencyRanges = compiledModel.GetResolutionRanges();
        if (dependencyRanges.Count > 0)
        {
            dependencyResolveResult = await _repositoryDependencyResolver.SoftResolveDependenciesAsync(
                    RangeRetainingDependencyRanges.For(dependencyRanges, compiledModel.IsRangeRetaining), modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        compiledModel = await BindForResolveAsync(compiledModel, modelGraph).ConfigureAwait(false);
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

        // AB#5665: a range-retaining model resolves its dependencies by range + floor, otherwise by exact pin.
        var dependencyRanges = compiledModel.GetResolutionRanges();
        if (dependencyRanges.Count > 0)
        {
            await _repositoryDependencyResolver.HardResolveDependenciesAsync(
                    RangeRetainingDependencyRanges.For(dependencyRanges, compiledModel.IsRangeRetaining), modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        compiledModel = await BindForResolveAsync(compiledModel, modelGraph).ConfigureAwait(false);
        Resolve(compiledModel, modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }
}