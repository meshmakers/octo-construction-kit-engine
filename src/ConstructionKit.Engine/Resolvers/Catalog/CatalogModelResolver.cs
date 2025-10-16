using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;

/// <summary>
///     Resolver that resolves the elements of a compiled model.
/// </summary>
internal class CatalogModelResolver : ModelResolver, ICatalogModelResolver
{
    private readonly ICatalogDependencyResolver _catalogDependencyResolver;

    /// <summary>
    ///     Creates a new instance of <see cref="CatalogModelResolver" />.
    /// </summary>
    /// <param name="catalogDependencyResolver"></param>
    /// <param name="inheritanceResolver"></param>
    /// <param name="elementResolver"></param>
    /// <param name="referenceResolver"></param>
    /// <param name="variableResolver"></param>
    public CatalogModelResolver(
        ICatalogDependencyResolver catalogDependencyResolver,
        IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver, IReferenceResolver referenceResolver, IVariableResolver variableResolver) : base(inheritanceResolver, elementResolver, referenceResolver,
        variableResolver)
    {
        _catalogDependencyResolver = catalogDependencyResolver;
    }

    public async Task<CkModelGraph> HardResolveAsync(ICollection<CkModelId> ckModelIds,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();
        await _catalogDependencyResolver.HardResolveDependenciesAsync(ckModelIds.ToList(),
                modelGraph, _variableResolver,
                originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);

        _referenceResolver.Resolve(modelGraph, originFileResolver, operationResult);
        _inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }


    public async Task<(CkModelGraph, CkCompiledModelRoot)> CompileAsync(CkModelCompileCandidate compileCandidate,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();
        IReadOnlyCollection<CkModelId> resolvedModelIds = [];

        if (compileCandidate.DependencyRanges != null)
        {
            resolvedModelIds = await _catalogDependencyResolver.HardResolveDependenciesAsync(compileCandidate.DependencyRanges,
                    modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        Resolve(compileCandidate, modelGraph, originFileResolver, operationResult);

        var compiledModel = new CkCompiledModelRoot
        {
            ModelId = compileCandidate.ModelId,
            Dependencies = resolvedModelIds.ToList(),
            Description = compileCandidate.Description,
            Types = compileCandidate.Types,
            Attributes = compileCandidate.Attributes,
            AssociationRoles = compileCandidate.AssociationRoles,
            Records = compileCandidate.Records,
            Enums = compileCandidate.Enums
        };

        return (modelGraph, compiledModel);
    }

    public async Task<CkModelGraph> HardResolveAsync(CkCompiledModelRoot compiledModel,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();

        if (compiledModel.Dependencies != null)
        {
            await _catalogDependencyResolver.HardResolveDependenciesAsync(
                    compiledModel.Dependencies, modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        Resolve(compiledModel, modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }
}