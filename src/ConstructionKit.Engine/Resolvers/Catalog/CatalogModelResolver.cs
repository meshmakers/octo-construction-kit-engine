using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;

/// <summary>
///     Resolver that resolves the elements of a compiled model.
/// </summary>
internal class CatalogModelResolver : ModelResolver, ICatalogModelResolver
{
    private readonly ICatalogDependencyResolver _catalogDependencyResolver;
    private readonly IOptions<CkCompilerOptions> _compilerOptions;
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly Lazy<ICatalogManager> _catalogManager;
    private readonly ILogger<CatalogModelResolver> _logger;

    /// <summary>
    ///     Creates a new instance of <see cref="CatalogModelResolver" />.
    /// </summary>
    public CatalogModelResolver(
        ICatalogDependencyResolver catalogDependencyResolver,
        IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver, IReferenceResolver referenceResolver, IVariableResolver variableResolver,
        IOptions<CkCompilerOptions> compilerOptions, ICkJsonSerializer ckJsonSerializer,
        Lazy<ICatalogManager> catalogManager, ILogger<CatalogModelResolver>? logger = null)
        : base(inheritanceResolver, elementResolver, referenceResolver, variableResolver)
    {
        _catalogDependencyResolver = catalogDependencyResolver;
        _compilerOptions = compilerOptions;
        _ckJsonSerializer = ckJsonSerializer;
        _catalogManager = catalogManager;
        _logger = logger ?? NullLogger<CatalogModelResolver>.Instance;
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

        // AB#5528: Secret value type rules apply to the model being compiled (dependency models
        // were validated when they were compiled). Also normalises Secret ownership in the DTOs
        // before they are written to the compiled model below.
        SecretAttributeValidator.Validate(compileCandidate, compileCandidate.DependencyRanges, modelGraph,
            originFileResolver, operationResult);

        var compiledModel = new CkCompiledModelRoot
        {
            ModelId = compileCandidate.ModelId,
            Dependencies = resolvedModelIds.ToList(),
            Description = compileCandidate.Description,
            Types = compileCandidate.Types,
            Attributes = compileCandidate.Attributes,
            AssociationRoles = compileCandidate.AssociationRoles,
            Records = compileCandidate.Records,
            Enums = compileCandidate.Enums,
            // CK v2 (AB#5667 / AB#5584)
            Interfaces = compileCandidate.Interfaces,
            CkLanguage = compileCandidate.CkLanguage
        };

        if (_compilerOptions.Value.RangeRetention && compileCandidate.DependencyRanges is { Count: > 0 } &&
            !operationResult.HasErrors && !operationResult.HasFatalErrors)
        {
            compiledModel = await ApplyRangeRetentionAsync(compileCandidate.DependencyRanges, compiledModel,
                modelGraph, sourceIdentifier).ConfigureAwait(false);
        }

        return (modelGraph, compiledModel);
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
            await _catalogDependencyResolver.HardResolveDependenciesAsync(
                    dependencyRanges.ToList(), modelGraph,
                    _variableResolver,
                    originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        // AB#5665: bind major-qualified references (System@2/...) to the resolved versions on a copy, so the
        // caller's instance keeps its version-less form.
        if (CkReferenceRewriter.HasMajorQualifiedReferences(compiledModel))
        {
            compiledModel = await CkCompiledModelCloner.CloneAsync(_ckJsonSerializer, compiledModel)
                .ConfigureAwait(false);
            CkReferenceRewriter.BindMajorQualified(compiledModel, modelGraph.Models.Keys);
        }

        Resolve(compiledModel, modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }

    /// <summary>
    ///     AB#5664: turns the exact-pinned compile result into its range-retaining form. The model was resolved
    ///     and validated against the highest catalog versions as before (the returned graph stays concrete); the
    ///     OUTPUT is a copy whose references into dependencies are major-qualified and which carries the
    ///     declared range plus floor of every direct dependency. Fails if the model references an element that
    ///     does not exist at the floor version of its dependency.
    /// </summary>
    private async Task<CkCompiledModelRoot> ApplyRangeRetentionAsync(
        IReadOnlyCollection<CkModelIdVersionRange> declaredRanges, CkCompiledModelRoot compiledModel,
        CkModelGraph modelGraph, object? sourceIdentifier)
    {
        var ownName = compiledModel.ModelId.Name;
        var dependencies = declaredRanges.Select(range => new CkModelDependencyDto
        {
            Range = range,
            Floor = (range.ModelVersionRange.MinVersion ?? new CkVersion(0, 0, 0)).ToString()
        }).ToList();

        await VerifyFloorsAsync(compiledModel, dependencies, modelGraph, sourceIdentifier).ConfigureAwait(false);

        var dependencyNames = new HashSet<string>(modelGraph.Models.Keys
            .Where(m => m.Name != ownName)
            .Select(m => m.Name));

        var output = await CkCompiledModelCloner.CloneAsync(_ckJsonSerializer, compiledModel).ConfigureAwait(false);
        CkReferenceRewriter.Rewrite(output, id => id.Name != ownName && !id.IsMajorQualified &&
                                                  dependencyNames.Contains(id.Name)
            ? id.ToMajorQualified()
            : null);
        output.DependencyRanges = dependencies;
        return output;
    }

    /// <summary>
    ///     Compile against the floor, verify against the highest (concept §4.3.1): the model was resolved
    ///     against the highest version of each dependency; every element it references in a dependency must
    ///     also exist at the floor version, or a tenant that has only the floor installed would accept a model
    ///     it cannot resolve.
    /// </summary>
    private async Task VerifyFloorsAsync(CkCompiledModelRoot compiledModel,
        IReadOnlyCollection<CkModelDependencyDto> dependencies, CkModelGraph modelGraph, object? sourceIdentifier)
    {
        var references = CkReferenceRewriter.CollectReferences(compiledModel);
        foreach (var dependency in dependencies)
        {
            var name = dependency.Range.Name;
            var resolved = modelGraph.Models.Keys.FirstOrDefault(m => m.Name == name);
            var referenced = references.Where(r => r.ModelId.Name == name).ToList();
            if (resolved == null || referenced.Count == 0 || resolved.Version == dependency.FloorVersion)
            {
                continue;
            }

            var floorModel = await TryGetFloorModelAsync(dependency, sourceIdentifier).ConfigureAwait(false);
            if (floorModel == null)
            {
                _logger.LogWarning(
                    "Range retention: no catalog holds a version of {Model} in {Range} at or above floor {Floor}; " +
                    "references of {CompiledModel} were verified against {Resolved} only",
                    name, dependency.Range, dependency.Floor, compiledModel.ModelId, resolved);
                continue;
            }

            if (floorModel.ModelId == resolved)
            {
                continue;
            }

            var available = new HashSet<(string, string)>(
                (floorModel.Types ?? []).Select(t => ("type", t.TypeId.ToString()!))
                .Concat((floorModel.Attributes ?? []).Select(a => ("attribute", a.AttributeId.ToString()!)))
                .Concat((floorModel.AssociationRoles ?? []).Select(r => ("association role", r.AssociationRoleId.ToString()!)))
                .Concat((floorModel.Records ?? []).Select(r => ("record", r.RecordId.ToString()!)))
                .Concat((floorModel.Enums ?? []).Select(e => ("enum", e.EnumId.ToString()!)))
                .Concat((floorModel.Interfaces ?? []).Select(i => ("interface", i.InterfaceId.ToString()!))));

            var missing = referenced
                .Where(r => !available.Contains((r.Kind, r.ElementId)))
                .Select(r => $"{r.Kind} {name}/{r.ElementId}")
                .Distinct()
                .ToList();
            if (missing.Count > 0)
            {
                throw ModelValidationException.ReferenceMissingAtFloor(compiledModel.ModelId, dependency.Range,
                    floorModel.ModelId, resolved, missing);
            }
        }
    }

    private async Task<CkCompiledModelRoot?> TryGetFloorModelAsync(CkModelDependencyDto dependency,
        object? sourceIdentifier)
    {
        var name = dependency.Range.Name;
        var operationResult = new OperationResult();
        var exact = await _catalogManager.Value
            .TryGetAsync(new CkModelId(name, dependency.FloorVersion), operationResult, sourceIdentifier)
            .ConfigureAwait(false);
        if (exact != null)
        {
            return exact;
        }

        // The floor itself was never published (e.g. range [2.0,3.0) but the catalogs start at 2.2.0):
        // the lowest available version inside the effective range is the oldest a tenant can have.
        var effectiveRange = dependency.GetEffectiveRange();
        var lowest = (await _catalogManager.Value.ListVersionsAsync(name, sourceIdentifier).ConfigureAwait(false))
            .Select(v => v.ModelId)
            .Where(effectiveRange.IsSatisfiedBy)
            .OrderBy(v => v.Version)
            .FirstOrDefault();
        return lowest == null
            ? null
            : await _catalogManager.Value.TryGetAsync(lowest, operationResult, sourceIdentifier).ConfigureAwait(false);
    }
}
