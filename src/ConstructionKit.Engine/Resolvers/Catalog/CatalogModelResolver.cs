using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Versioning;
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
            resolvedModelIds = await _catalogDependencyResolver.HardResolveDependenciesAsync(
                    RangeRetainingDependencyRanges.For(compileCandidate.DependencyRanges,
                        _compilerOptions.Value.RangeRetention),
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

        // F1.1-S6: ckLanguage 2 and range-retaining output name the lowest engine that can read them. Review G3 E-M4:
        // so does a model whose dependencies need it — an older engine could not resolve them, so the model goes to
        // ck-models/v3 as well (CkCatalogLayout.RequiresV3 follows MinEngineVersion).
        var required = new List<string?> { CkEngineVersion.GetRequiredMinEngineVersion(compiledModel) };
        foreach (var dependencyId in resolvedModelIds)
        {
            var dependency = await _catalogManager.Value
                .TryGetAsync(dependencyId, new OperationResult(), sourceIdentifier).ConfigureAwait(false);
            if (dependency != null)
            {
                required.Add(dependency.MinEngineVersion ?? CkEngineVersion.GetRequiredMinEngineVersion(dependency));
            }
        }

        compiledModel.MinEngineVersion = CkEngineVersion.Max(required);

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
                    RangeRetainingDependencyRanges.For(dependencyRanges, compiledModel.IsRangeRetaining), modelGraph,
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
            Floor = FloorOf(range.ModelVersionRange).ToString()
        }).ToList();

        // H6: references are stored as Name@<major>. A range that admits more than one major (e.g. "Basic"
        // = >=1.0.0, or [2.0,) ) would let a tenant resolve a major the references cannot bind to.
        var multiMajor = dependencies.Where(d => !IsWithinOneMajor(d)).Select(d => d.Range).ToList();
        if (multiMajor.Count > 0)
        {
            throw ModelValidationException.RangeSpansSeveralMajors(compiledModel.ModelId, multiMajor);
        }

        await VerifyFloorsAsync(compiledModel, dependencies, modelGraph, sourceIdentifier, null).ConfigureAwait(false);

        // Review L14: references into TRANSITIVE dependencies (e.g. Industry.Energy uses ${Basic} but declares only
        // Industry.Basic) are floor-checked too, against the floor the intermediate model guarantees.
        var (transitive, via) = await CollectTransitiveDependenciesAsync(compiledModel, dependencies, modelGraph,
            sourceIdentifier).ConfigureAwait(false);
        await VerifyFloorsAsync(compiledModel, transitive, modelGraph, sourceIdentifier, via).ConfigureAwait(false);

        var dependencyNames = new HashSet<string>(modelGraph.Models.Keys
            .Where(m => m.Name != ownName)
            .Select(m => m.Name));

        var output = await CkCompiledModelCloner.CloneAsync(_ckJsonSerializer, compiledModel).ConfigureAwait(false);
        CkReferenceRewriter.Rewrite(output, id => id.Name != ownName && !id.IsMajorQualified &&
                                                  dependencyNames.Contains(id.Name)
            ? id.ToMajorQualified()
            : null);
        output.DependencyRanges = dependencies;
        // AB#4472: which elements and members of each declared dependency the model uses.
        CkUsedSurfaceCollector.Apply(output, modelGraph);
        return output;
    }

    /// <summary>
    ///     The floor of a declared range: its lower bound; for an exclusive lower bound (<c>(2.4,3.0)</c>) the
    ///     next patch, so the floor itself is inside the range (review L13).
    /// </summary>
    internal static CkVersion FloorOf(CkVersionRange range)
    {
        var min = range.MinVersion ?? new CkVersion(0, 0, 0);
        return range.MinInclusive || range.MinVersion == null
            ? min
            : new CkVersion(min.Major, min.Minor, min.Revision + 1);
    }

    /// <summary>
    ///     True when every version the dependency admits (range ∩ ≥ floor) has the floor's major.
    /// </summary>
    internal static bool IsWithinOneMajor(CkModelDependencyDto dependency)
    {
        var range = dependency.Range.ModelVersionRange;
        var nextMajor = new CkVersion(dependency.FloorVersion.Major + 1, 0, 0);
        if (range.MaxVersion == null)
        {
            return false;
        }

        var comparison = range.MaxVersion.Value.CompareTo(nextMajor);
        return comparison < 0 || (comparison == 0 && !range.MaxInclusive);
    }

    /// <summary>
    ///     Compile against the floor, verify against the highest (concept §4.3.1): the model was resolved
    ///     against the highest version of each dependency; every element it references in a dependency must
    ///     also exist at the floor version, or a tenant that has only the floor installed would accept a model
    ///     it cannot resolve.
    /// </summary>
    private async Task VerifyFloorsAsync(CkCompiledModelRoot compiledModel,
        IReadOnlyCollection<CkModelDependencyDto> dependencies, CkModelGraph modelGraph, object? sourceIdentifier,
        IReadOnlyDictionary<string, CkModelId>? transitiveVia)
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
                    floorModel.ModelId, resolved, missing,
                    transitiveVia != null && transitiveVia.TryGetValue(name, out var viaModel) ? viaModel : null);
            }
        }
    }

    /// <summary>
    ///     Review L14: for every model the compiled model references but does not declare, the range + floor the
    ///     resolved intermediate models guarantee (their range-retaining dependency, or their exact pin as
    ///     <c>[v]</c> with floor v). The highest floor wins — it is the oldest version a tenant can hold while the
    ///     intermediate is installed.
    /// </summary>
    private async Task<(IReadOnlyCollection<CkModelDependencyDto> Dependencies, IReadOnlyDictionary<string, CkModelId> Via)>
        CollectTransitiveDependenciesAsync(CkCompiledModelRoot compiledModel,
            IReadOnlyCollection<CkModelDependencyDto> declared, CkModelGraph modelGraph, object? sourceIdentifier)
    {
        var ownName = compiledModel.ModelId.Name;
        var declaredNames = new HashSet<string>(declared.Select(d => d.Range.Name));
        var undeclared = CkReferenceRewriter.CollectReferences(compiledModel)
            .Select(r => r.ModelId.Name)
            .Where(n => n != ownName && !declaredNames.Contains(n))
            .Distinct()
            .ToList();
        var result = new List<CkModelDependencyDto>();
        var via = new Dictionary<string, CkModelId>();
        if (undeclared.Count == 0)
        {
            return (result, via);
        }

        var intermediates = new List<CkCompiledModelRoot>();
        foreach (var modelId in modelGraph.Models.Keys.Where(m => m.Name != ownName))
        {
            var model = await _catalogManager.Value.TryGetAsync(modelId, new OperationResult(), sourceIdentifier)
                .ConfigureAwait(false);
            if (model != null)
            {
                intermediates.Add(model);
            }
        }

        foreach (var name in undeclared)
        {
            CkModelDependencyDto? best = null;
            CkModelId? bestVia = null;
            foreach (var intermediate in intermediates.Where(m => m.ModelId.Name != name))
            {
                var candidate = intermediate.DependencyRanges?.FirstOrDefault(d => d.Range.Name == name);
                if (candidate == null && intermediate.Dependencies?.FirstOrDefault(d => d.Name == name) is { } pin)
                {
                    candidate = new CkModelDependencyDto
                    {
                        Range = new CkModelIdVersionRange(name, $"[{pin.Version}]"), Floor = pin.Version.ToString()
                    };
                }

                if (candidate != null && (best == null || candidate.FloorVersion.CompareTo(best.FloorVersion) > 0))
                {
                    best = candidate;
                    bestVia = intermediate.ModelId;
                }
            }

            if (best != null)
            {
                result.Add(best);
                via[name] = bestVia!;
            }
        }

        return (result, via);
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
