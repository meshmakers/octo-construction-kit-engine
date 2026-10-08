using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Implementation of <see cref="IInheritanceResolver" /> that resolves the inheritance of a compiled model.
/// </summary>
internal class InheritanceResolver : IInheritanceResolver
{
    private readonly ILogger<InheritanceResolver> _logger;

    /// <summary>
    ///     Creates a new instance of the <see cref="InheritanceResolver" /> class.
    /// </summary>
    /// <param name="logger"></param>
    public InheritanceResolver(ILogger<InheritanceResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public CkModelGraph Resolve(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult, ISet<CkModelId>? failedModelIds = null)
    {
        _logger.LogDebug("Starting resolving inheritance");

        HashSet<CkId<CkRecordId>> handledRecordHashSet = [];
        HashSet<CkId<CkTypeId>> handledTypesHashSet = [];
        HashSet<CkId<CkTypeId>> failedTypeIds = [];

        foreach (var ckTypeKeyValue in modelGraph.Types)
        {
            _logger.LogDebug("Resolving inheritance for type {CkTypeId}", ckTypeKeyValue.Key);

            if (failedModelIds != null)
            {
                var typeGraph = GetAndUpdateTypeGraphSafe(handledTypesHashSet, failedTypeIds, modelGraph,
                    ckTypeKeyValue.Key, originFileResolver, operationResult, failedModelIds);
                if (typeGraph == null)
                {
                    continue;
                }

                GetDirectedAggregationsAndAttributesSafe(handledTypesHashSet, failedTypeIds, modelGraph,
                    ckTypeKeyValue.Value, originFileResolver, operationResult, failedModelIds);
            }
            else
            {
                GetAndUpdateTypeGraph(handledTypesHashSet, modelGraph, ckTypeKeyValue.Key, originFileResolver,
                    operationResult);
                GetDirectedAggregationsAndAttributes(handledTypesHashSet, modelGraph, ckTypeKeyValue.Value,
                    originFileResolver, operationResult);
            }
        }

        foreach (var ckRecordKeyValue in modelGraph.Records)
        {
            _logger.LogDebug("Resolving inheritance for record {CkRecordId}", ckRecordKeyValue.Key);

            if (failedModelIds != null)
            {
                var recordGraph = GetAndUpdateRecordGraphSafe(handledRecordHashSet, modelGraph, ckRecordKeyValue.Key,
                    originFileResolver, operationResult, failedModelIds);
                if (recordGraph != null)
                {
                    GetDirectedRecordAttributes(modelGraph, recordGraph, originFileResolver, operationResult);
                }
            }
            else
            {
                var recordGraph = GetAndUpdateRecordGraph(handledRecordHashSet, modelGraph, ckRecordKeyValue.Key,
                    originFileResolver, operationResult);
                GetDirectedRecordAttributes(modelGraph, recordGraph, originFileResolver, operationResult);
            }
        }

        _logger.LogDebug("Resolving dependencies based on inheritance");
        BuildInheritedConfiguration(modelGraph, failedTypeIds, originFileResolver, operationResult);

        _logger.LogDebug("Resolving interfaces and methods");
        ResolveInterfacesAndMethods(modelGraph, failedTypeIds, originFileResolver, operationResult);

        _logger.LogDebug("Validating display rules");
        ValidateDisplayRules(modelGraph, failedTypeIds, originFileResolver, operationResult);

        _logger.LogDebug("Resolving inheritance completed");

        return modelGraph;
    }

    private static readonly HashSet<string> ReservedMethodNames =
        new(StringComparer.OrdinalIgnoreCase) { "Create", "Update", "Delete" };

    /// <summary>
    ///     CK v2 (AB#5667 / AB#5669): completes <see cref="CkTypeGraph.AllImplementedInterfaces" /> (own ∪ base) and
    ///     <see cref="CkTypeGraph.AllMethods" /> (base ∪ own) along the whole base-type chain, checks the interface
    ///     rules I-1..I-4 and the method rules M-1..M-5 at the declaring type, and fills
    ///     <see cref="CkInterfaceGraph.ImplementingTypes" />. Runs after attribute flattening, so members a type
    ///     inherits satisfy an interface.
    /// </summary>
    private static void ResolveInterfacesAndMethods(CkModelGraph modelGraph, HashSet<CkId<CkTypeId>> failedTypeIds,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        ResolveInterfaceHierarchy(modelGraph);

        foreach (var pair in modelGraph.Types)
        {
            var ckTypeId = pair.Key;
            var typeGraph = pair.Value;
            if (failedTypeIds.Contains(ckTypeId))
            {
                continue;
            }

            var location = originFileResolver.Resolve(ckTypeId);
            var baseGraphs = typeGraph.BaseTypes
                .Select(b => modelGraph.Types.TryGetValue(b.BaseCkTypeId, out var g) ? g : null)
                .Where(g => g != null)
                .Select(g => g!)
                .ToList();

            // Inheritance: nearest base first, so the nearest declaration of a method id wins.
            foreach (var baseGraph in baseGraphs)
            {
                typeGraph.InheritInterfaces(baseGraph.DeclaredImplements);
                typeGraph.InheritMethods(baseGraph.DefinedMethods.Select(m => new CkMethodGraph(baseGraph.CkTypeId, m)));
            }

            ValidateMethods(typeGraph, baseGraphs, ckTypeId, location, operationResult);

            // F1.1-S5: implementing an interface implements every interface it extends.
            typeGraph.InheritInterfaces(typeGraph.AllImplementedInterfaces
                .SelectMany(i => modelGraph.Interfaces.TryGetValue(i, out var g) ? g.AllExtendedInterfaces : [])
                .ToList());

            foreach (var ckInterfaceId in typeGraph.DeclaredImplements)
            {
                if (modelGraph.Interfaces.TryGetValue(ckInterfaceId, out var interfaceGraph))
                {
                    ValidateImplementation(typeGraph, interfaceGraph, ckTypeId, location, operationResult);
                }
            }

            foreach (var ckInterfaceId in typeGraph.AllImplementedInterfaces)
            {
                if (modelGraph.Interfaces.TryGetValue(ckInterfaceId, out var interfaceGraph))
                {
                    interfaceGraph.AddImplementingType(ckTypeId);
                }
            }
        }
    }

    /// <summary>
    ///     CK v2 (F1.1-S5): computes <see cref="CkInterfaceGraph.AllExtendedInterfaces" /> (transitive, in
    ///     declaration order, depth first) and the inherited members of every interface. Unknown entries and cycles
    ///     are skipped here; the compiler rules report them (F1.2-S4).
    /// </summary>
    private static void ResolveInterfaceHierarchy(CkModelGraph modelGraph)
    {
        foreach (var interfaceGraph in modelGraph.Interfaces.Values)
        {
            var all = new List<CkId<CkInterfaceId>>();
            var visited = new HashSet<CkId<CkInterfaceId>> { interfaceGraph.CkInterfaceId };

            void Visit(IEnumerable<CkId<CkInterfaceId>> extends)
            {
                foreach (var extended in extends)
                {
                    if (!visited.Add(extended) || !modelGraph.Interfaces.TryGetValue(extended, out var extendedGraph))
                    {
                        continue;
                    }

                    all.Add(extended);
                    Visit(extendedGraph.DeclaredExtends);
                }
            }

            Visit(interfaceGraph.DeclaredExtends);
            interfaceGraph.SetInheritedMembers(all, all.Select(i => modelGraph.Interfaces[i]));
        }
    }

    /// <summary>
    ///     CK v2 (AB#5667) rules I-1..I-4 for one declared <c>implements</c> entry.
    /// </summary>
    private static void ValidateImplementation(CkTypeGraph typeGraph, CkInterfaceGraph interfaceGraph,
        CkId<CkTypeId> ckTypeId, string location, OperationResult operationResult)
    {
        var ckInterfaceId = interfaceGraph.CkInterfaceId;
        // The merged members (ReferenceResolver): unknown (94) and duplicate (127) members are already reported
        // there and must not produce follow-up implementation errors.
        // F1.1-S5: inherited members (extends) are part of the contract.
        foreach (var member in interfaceGraph.AllAttributes.Values)
        {
            if (!typeGraph.AllAttributes.TryGetValue(member.CkAttributeId, out var assignment))
            {
                // I-1
                if (!member.IsOptional)
                {
                    operationResult.AddMessage(MessageCodes.CkInterfaceMemberMissing(location, ckTypeId,
                        ckInterfaceId, member.CkAttributeId, member.AttributeName));
                }

                continue;
            }

            // I-2
            if (!member.IsOptional && assignment.IsOptional)
            {
                operationResult.AddMessage(MessageCodes.CkInterfaceMemberMultiplicityMismatch(location, ckTypeId,
                    ckInterfaceId, member.CkAttributeId, member.AttributeName));
            }

            // I-3
            if (!string.Equals(assignment.AttributeName, member.AttributeName, StringComparison.Ordinal))
            {
                operationResult.AddMessage(MessageCodes.CkInterfaceMemberNameMismatch(location, ckTypeId,
                    ckInterfaceId, member.CkAttributeId, assignment.AttributeName, member.AttributeName));
            }

            // I-4
            if (assignment.Access == CkAttributeAccessDto.Hidden)
            {
                operationResult.AddMessage(MessageCodes.CkInterfaceMemberHidden(location, ckTypeId, ckInterfaceId,
                    member.CkAttributeId, member.AttributeName));
            }
        }
    }

    /// <summary>
    ///     CK v2 (AB#5669) rules M-1..M-5 for the methods declared on a type.
    /// </summary>
    private static void ValidateMethods(CkTypeGraph typeGraph, IReadOnlyCollection<CkTypeGraph> baseGraphs,
        CkId<CkTypeId> ckTypeId, string location, OperationResult operationResult)
    {
        // M-1: unique per type, and no re-declaration (override) of an inherited method id.
        foreach (var duplicate in typeGraph.DefinedMethods.GroupBy(m => m.MethodId).Where(g => g.Count() > 1))
        {
            operationResult.AddMessage(MessageCodes.CkMethodIdNotUnique(location, duplicate.Key, ckTypeId,
                "the method id is declared more than once on the type"));
        }

        foreach (var method in typeGraph.DefinedMethods)
        {
            var declaringBase = baseGraphs.FirstOrDefault(b => b.DefinedMethods.Any(m => m.MethodId == method.MethodId));
            if (declaringBase != null)
            {
                operationResult.AddMessage(MessageCodes.CkMethodIdNotUnique(location, method.MethodId, ckTypeId,
                    $"it is inherited from '{declaringBase.CkTypeId}'; overriding an inherited method is not supported"));
            }

            // M-2
            var methodName = GetMethodName(method.MethodId);
            if (ReservedMethodNames.Contains(methodName))
            {
                operationResult.AddMessage(MessageCodes.CkMethodNameReserved(location, method.MethodId, ckTypeId,
                    methodName));
            }

            // M-3
            foreach (var duplicate in (method.Parameters ?? []).GroupBy(p => p.Name).Where(g => g.Count() > 1))
            {
                operationResult.AddMessage(MessageCodes.CkMethodParameterInvalid(location, method.MethodId, ckTypeId,
                    $"parameter name '{duplicate.Key}' is not unique"));
            }

            foreach (var parameter in method.Parameters ?? [])
            {
                ValidateValueType($"parameter '{parameter.Name}'", parameter.ValueType, parameter.ValueCkRecordId,
                    parameter.ValueCkEnumId, method.MethodId, ckTypeId, location, operationResult);
            }

            if (method.Result != null)
            {
                ValidateValueType("the result", method.Result.ValueType, method.Result.ValueCkRecordId,
                    method.Result.ValueCkEnumId, method.MethodId, ckTypeId, location, operationResult);
            }

            // M-4
            foreach (var duplicate in (method.Errors ?? []).GroupBy(e => e.Code).Where(g => g.Count() > 1))
            {
                operationResult.AddMessage(MessageCodes.CkMethodErrorCodeInvalid(location, duplicate.Key,
                    method.MethodId, ckTypeId, "the code is declared more than once"));
            }

            foreach (var error in (method.Errors ?? []).Where(e =>
                         e.Code.StartsWith("METHOD_", StringComparison.Ordinal)))
            {
                operationResult.AddMessage(MessageCodes.CkMethodErrorCodeInvalid(location, error.Code,
                    method.MethodId, ckTypeId, "the prefix 'METHOD_' is reserved for platform errors"));
            }

            // M-5
            if (method.Kind == CkMethodKindDto.Static && method.Authorization?.AllowSelf == true)
            {
                operationResult.AddMessage(MessageCodes.CkMethodAuthorizationInvalid(location, method.MethodId,
                    ckTypeId, "'allowSelf: true' requires an instance method, a static method has no target entity"));
            }
        }
    }

    private static void ValidateValueType(string what, AttributeValueTypesDto valueType, CkId<CkRecordId>? recordId,
        CkId<CkEnumId>? enumId, string methodId, CkId<CkTypeId> ckTypeId, string location,
        OperationResult operationResult)
    {
        string? reason = null;
        if (valueType == AttributeValueTypesDto.Record && recordId == null)
        {
            reason = $"{what} is of value type Record but declares no 'valueCkRecordId'";
        }
        else if (valueType != AttributeValueTypesDto.Record && recordId != null)
        {
            reason = $"{what} declares 'valueCkRecordId' but is of value type {valueType}";
        }
        else if (valueType == AttributeValueTypesDto.Enum && enumId == null)
        {
            reason = $"{what} is of value type Enum but declares no 'valueCkEnumId'";
        }
        else if (valueType != AttributeValueTypesDto.Enum && enumId != null)
        {
            reason = $"{what} declares 'valueCkEnumId' but is of value type {valueType}";
        }

        if (reason != null)
        {
            operationResult.AddMessage(MessageCodes.CkMethodParameterInvalid(location, methodId, ckTypeId, reason));
        }
    }

    /// <summary>
    ///     The method name without the element version: <c>ChangePassword-1</c> → <c>ChangePassword</c>.
    /// </summary>
    private static string GetMethodName(string methodId)
    {
        var index = methodId.LastIndexOf('-');
        return index > 0 ? methodId.Substring(0, index) : methodId;
    }

    /// <summary>
    /// Safe version of GetAndUpdateTypeGraph that does not throw on missing types.
    /// Instead, it records the failed model ID and returns null.
    /// </summary>
    private CkTypeGraph? GetAndUpdateTypeGraphSafe(HashSet<CkId<CkTypeId>> handledTypesHashSet,
        HashSet<CkId<CkTypeId>> failedTypeIds,
        CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId,
        IOriginFileResolver originFileResolver, OperationResult operationResult, ISet<CkModelId> failedModelIds)
    {
        if (failedTypeIds.Contains(ckTypeId))
        {
            return null;
        }

        if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknown(originFileResolver.Resolve(ckTypeId), ckTypeId));
            failedModelIds.Add(ckTypeId.ModelId);
            failedTypeIds.Add(ckTypeId);
            return null;
        }

        if (!handledTypesHashSet.Contains(ckTypeId))
        {
            var baseTypes = GetBaseTypesSafe(modelGraph, ckTypeId, originFileResolver, operationResult, failedModelIds,
                failedTypeIds);
            if (baseTypes == null)
            {
                return null;
            }

            typeGraph.AddBaseTypes(baseTypes);

            if (baseTypes.Any() && baseTypes.All(t =>
                    CompilerStatics.WhiteListedCkTypeIds.Any(v => v.IsSatisfiedBy(t.BaseCkTypeId))))
            {
                typeGraph.SetIsCollectionRoot(true);
                typeGraph.SetDefiningCollectionCkTypeId(typeGraph.CkTypeId);
            }

            ResolveDisplayRules(modelGraph, typeGraph, baseTypes);

            foreach (var ckTypeAttribute in typeGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(
                        originFileResolver.Resolve(ckTypeId),
                        ckTypeAttribute.CkAttributeId, ckTypeId));
                    continue;
                }

                typeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledTypesHashSet.Add(ckTypeId);
        }

        return typeGraph;
    }

    /// <summary>
    /// Validates the display rules declared on types: syntax (${path} interpolation with ?? coalesce)
    /// and that every referenced attribute path exists on the type (own attributes including record
    /// paths, no associations). Runs after attribute flattening so inherited attributes are visible.
    /// Errors are reported only at the declaring type — derived types inheriting the rule share the
    /// same attribute set (attribute merge is additive), so a rule valid at the declaring type is
    /// valid for all inheritors.
    /// </summary>
    private static void ValidateDisplayRules(CkModelGraph modelGraph, HashSet<CkId<CkTypeId>> failedTypeIds,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        foreach (var pair in modelGraph.Types)
        {
            var ckTypeId = pair.Key;
            var typeGraph = pair.Value;
            if (failedTypeIds.Contains(ckTypeId))
            {
                continue;
            }

            if (typeGraph.DisplayNameRuleDeclared)
            {
                ValidateDisplayRule(modelGraph, ckTypeId, typeGraph, typeGraph.DisplayNameRule!,
                    "displayNameRule", originFileResolver, operationResult);
            }

            if (typeGraph.DisplayDescriptionRuleDeclared)
            {
                ValidateDisplayRule(modelGraph, ckTypeId, typeGraph, typeGraph.DisplayDescriptionRule!,
                    "displayDescriptionRule", originFileResolver, operationResult);
            }

            if (typeGraph.OwnerAttributePathDeclared)
            {
                ValidateOwnerAttribute(modelGraph, ckTypeId, typeGraph, originFileResolver, operationResult);
            }

            ValidateRestrictedAttributeUse(modelGraph, ckTypeId, typeGraph, originFileResolver, operationResult);
        }
    }

    /// <summary>
    ///     Review M9 (CK v2): a Hidden attribute must not be reachable through derived or indirect fields —
    ///     a display rule feeds <c>rtDisplayName</c>/<c>rtDisplayDescription</c> (always readable and
    ///     filterable), a text index feeds full-text search, an owner path is compared against the caller.
    ///     Owner paths also reject MethodOnly (ownership must be settable by the generic path that creates the
    ///     entity). Message 105, reported at the declaring type.
    /// </summary>
    private static void ValidateRestrictedAttributeUse(CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId,
        CkTypeGraph typeGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var location = originFileResolver.Resolve(ckTypeId);

        void Check(string ruleProperty, string path, Func<CkAttributeAccessDto, bool> isRestricted)
        {
            var restricted = FindRestrictedSegment(modelGraph, typeGraph, path, isRestricted);
            if (restricted != null)
            {
                operationResult.AddMessage(MessageCodes.RestrictedAttributeInDerivedRule(location, ruleProperty,
                    path, ckTypeId, restricted.Value.Name, restricted.Value.Access));
            }
        }

        static bool IsHidden(CkAttributeAccessDto access) => access == CkAttributeAccessDto.Hidden;

        foreach (var (ruleProperty, rule, declared) in new[]
                 {
                     ("displayNameRule", typeGraph.DisplayNameRule, typeGraph.DisplayNameRuleDeclared),
                     ("displayDescriptionRule", typeGraph.DisplayDescriptionRule, typeGraph.DisplayDescriptionRuleDeclared)
                 })
        {
            if (!declared || string.IsNullOrWhiteSpace(rule))
            {
                continue;
            }

            var parseResult = DisplayRuleParser.Parse(rule!);
            if (!parseResult.IsValid)
            {
                continue; // reported by ValidateDisplayRule
            }

            foreach (var path in parseResult.ReferencedPaths)
            {
                Check(ruleProperty, path, IsHidden);
            }
        }

        if (typeGraph.OwnerAttributePathDeclared && !string.IsNullOrWhiteSpace(typeGraph.OwnerAttributePath))
        {
            Check("ownerAttributePath", typeGraph.OwnerAttributePath!,
                access => access is CkAttributeAccessDto.Hidden or CkAttributeAccessDto.MethodOnly);
        }

        // F1.2-S2 (review N5/N6): every index type, case-insensitive (Mongo resolves index paths that way), and in a
        // ckLanguage 2 model unknown paths are rejected instead of silently skipped.
        var rejectUnknownPaths = modelGraph.Models.TryGetValue(ckTypeId.ModelId, out var modelProperties) &&
                                 modelProperties.EffectiveCkLanguage >= 2;
        foreach (var index in typeGraph.Indexes)
        {
            foreach (var path in index.Fields.SelectMany(f => f.AttributePaths ?? []))
            {
                var walk = WalkAttributePath(modelGraph, typeGraph, path, IsHidden);
                if (walk.Restricted is { } restricted)
                {
                    operationResult.AddMessage(index.IndexType == IndexTypeDto.Text
                        ? MessageCodes.RestrictedAttributeInDerivedRule(location, "text index", path, ckTypeId,
                            restricted.Name, restricted.Access)
                        : MessageCodes.HiddenAttributeIndexed(location, index.IndexType, ckTypeId, restricted.Name,
                            path));
                }
                else if (walk.UnknownSegment is { } unknown && rejectUnknownPaths && !IsSystemIndexPath(path) &&
                         // A collection root also carries the text/ascending indexes merged from its derived types
                         // (CkTypeGraph.MergeTextIndexes); such a path is checked at the derived type itself.
                         !typeGraph.GetAllDerivedTypes(false).Any(d =>
                             modelGraph.Types.TryGetValue(d, out var derived) &&
                             WalkAttributePath(modelGraph, derived, path, IsHidden).UnknownSegment == null))
                {
                    operationResult.AddMessage(MessageCodes.UnknownIndexAttributePath(location, index.IndexType,
                        ckTypeId, path, unknown));
                }
            }
        }
    }

    /// <summary>
    ///     Index paths on entity system fields (<c>RtWellKnownName</c>, <c>RtBlueprintSource</c>, <c>CkTypeId</c>, ...)
    ///     are not attributes.
    /// </summary>
    private static bool IsSystemIndexPath(string path) =>
        path.StartsWith("Rt", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("CkTypeId", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Walks an attribute path (record segments allowed) and returns the first segment whose access is
    ///     restricted, or <c>null</c>. Unknown paths return <c>null</c> (reported elsewhere).
    /// </summary>
    private static (string Name, CkAttributeAccessDto Access)? FindRestrictedSegment(CkModelGraph modelGraph,
        CkTypeWithAttributesGraph scope, string path, Func<CkAttributeAccessDto, bool> isRestricted) =>
        WalkAttributePath(modelGraph, scope, path, isRestricted).Restricted;

    /// <summary>
    ///     Walks an attribute path (record segments allowed). Segments are matched case-insensitively (review N6:
    ///     Mongo resolves attribute paths with <c>OrdinalIgnoreCase</c>, so <c>passwordHash</c> reaches
    ///     <c>PasswordHash</c>). Returns the first restricted segment, or the first segment that names no attribute.
    /// </summary>
    private static ((string Name, CkAttributeAccessDto Access)? Restricted, string? UnknownSegment) WalkAttributePath(
        CkModelGraph modelGraph, CkTypeWithAttributesGraph scope, string path,
        Func<CkAttributeAccessDto, bool> isRestricted)
    {
        var current = scope;
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            if (!current.AllAttributesByName.TryGetValue(segments[i], out var attribute))
            {
                attribute = current.AllAttributesByName
                    .FirstOrDefault(a => string.Equals(a.Key, segments[i], StringComparison.OrdinalIgnoreCase)).Value;
                if (attribute == null)
                {
                    return (null, segments[i]);
                }
            }

            if (isRestricted(attribute.Access))
            {
                return ((attribute.AttributeName, attribute.Access), null);
            }

            if (i == segments.Length - 1)
            {
                return (null, null);
            }

            if (attribute.ValueCkRecordId == null ||
                !modelGraph.Records.TryGetValue(attribute.ValueCkRecordId, out var recordGraph))
            {
                // A further segment below a non-record attribute names nothing.
                return (null, segments[i + 1]);
            }

            current = recordGraph;
        }

        return (null, null);
    }

    /// <summary>
    /// Validates the owner attribute path declared on a type (AB#4978): dot-separated segments
    /// traverse single-valued Record attributes (RecordArray segments would make ownership
    /// multi-valued and are rejected); the terminal segment must be of value type String — the
    /// owned-only data-permission predicate compares its value against the caller's subject id.
    /// Runs after attribute flattening so inherited attributes are visible; like display rules,
    /// errors are reported only at the declaring type. Associations are not traversable.
    /// </summary>
    private static void ValidateOwnerAttribute(CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId,
        CkTypeGraph typeGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var location = originFileResolver.Resolve(ckTypeId);
        var ownerAttributePath = typeGraph.OwnerAttributePath!;

        var segments = ownerAttributePath.Split('.');
        CkTypeWithAttributesGraph scope = typeGraph;
        for (var i = 0; i < segments.Length; i++)
        {
            if (!scope.AllAttributesByName.TryGetValue(segments[i], out var attribute))
            {
                operationResult.AddMessage(MessageCodes.OwnerAttributeInvalid(location, ckTypeId,
                    ownerAttributePath,
                    $"segment '{segments[i]}' does not exist (associations are not supported)"));
                return;
            }

            if (i == segments.Length - 1)
            {
                if (attribute.ValueType == AttributeValueTypesDto.Secret)
                {
                    // AB#5528: a Secret attribute is stored encrypted and never compared or
                    // projected, so it can never identify the owner of an entity.
                    operationResult.AddMessage(MessageCodes.OwnerAttributeInvalid(location, ckTypeId,
                        ownerAttributePath,
                        "the terminal attribute is a Secret attribute - secrets cannot be owner attributes"));
                }
                else if (attribute.ValueType != AttributeValueTypesDto.String)
                {
                    operationResult.AddMessage(MessageCodes.OwnerAttributeInvalid(location, ckTypeId,
                        ownerAttributePath,
                        $"the terminal attribute must be of value type String, but is {attribute.ValueType}"));
                }

                return;
            }

            if (attribute.ValueType == AttributeValueTypesDto.RecordArray)
            {
                operationResult.AddMessage(MessageCodes.OwnerAttributeInvalid(location, ckTypeId,
                    ownerAttributePath,
                    $"segment '{segments[i]}' is a RecordArray — multi-valued ownership is not supported"));
                return;
            }

            if (attribute.ValueType != AttributeValueTypesDto.Record || attribute.ValueCkRecordId == null ||
                !modelGraph.Records.TryGetValue(attribute.ValueCkRecordId, out var recordGraph))
            {
                operationResult.AddMessage(MessageCodes.OwnerAttributeInvalid(location, ckTypeId,
                    ownerAttributePath,
                    $"segment '{segments[i]}' is not a Record attribute and cannot be traversed"));
                return;
            }

            scope = recordGraph;
        }
    }

    private static void ValidateDisplayRule(CkModelGraph modelGraph, CkId<CkTypeId> ckTypeId, CkTypeGraph typeGraph,
        string rule, string ruleProperty, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var location = originFileResolver.Resolve(ckTypeId);

        var parseResult = DisplayRuleParser.Parse(rule);
        if (!parseResult.IsValid)
        {
            foreach (var error in parseResult.Errors)
            {
                operationResult.AddMessage(MessageCodes.DisplayRuleSyntaxInvalid(location, ruleProperty, ckTypeId,
                    error));
            }

            return;
        }

        foreach (var attributePath in parseResult.ReferencedPaths)
        {
            if (!IsValidAttributePath(modelGraph, typeGraph, attributePath))
            {
                operationResult.AddMessage(MessageCodes.DisplayRuleAttributePathUnknown(location, ruleProperty,
                    ckTypeId, attributePath));
            }
        }
    }

    /// <summary>
    /// Checks an attribute path against a type's flattened attributes; dot-separated segments
    /// traverse record attributes (arbitrarily nested).
    /// </summary>
    private static bool IsValidAttributePath(CkModelGraph modelGraph, CkTypeWithAttributesGraph scope, string path)
    {
        var segments = path.Split('.');
        var current = scope;
        for (var i = 0; i < segments.Length; i++)
        {
            if (!current.AllAttributesByName.TryGetValue(segments[i], out var attribute))
            {
                return false;
            }

            if (i == segments.Length - 1)
            {
                return true;
            }

            if (attribute.ValueCkRecordId == null ||
                !modelGraph.Records.TryGetValue(attribute.ValueCkRecordId, out var recordGraph))
            {
                return false;
            }

            current = recordGraph;
        }

        return false;
    }

    /// <summary>
    /// Fills empty display rules from the base-type chain. The chain is ordered nearest-first, so the
    /// nearest declared (or already-inherited) rule wins; a rule declared on the type itself is never
    /// overwritten.
    /// </summary>
    private static void ResolveDisplayRules(CkModelGraph modelGraph, CkTypeGraph typeGraph,
        IEnumerable<CkGraphTypeInheritance> baseTypes)
    {
        foreach (var baseType in baseTypes)
        {
            if (!modelGraph.Types.TryGetValue(baseType.BaseCkTypeId, out var baseGraph))
            {
                continue;
            }

            typeGraph.InheritDisplayRules(baseGraph.DisplayNameRule, baseGraph.DisplayDescriptionRule);
            typeGraph.InheritOwnerAttribute(baseGraph.OwnerAttributePath);
        }
    }

    /// <summary>
    /// Fills an undeclared record key from the base-record chain (AB#5528, concept §4.6). The chain
    /// is ordered nearest-first, so the nearest declaring base wins; a key declared on the record
    /// itself is never overwritten.
    /// </summary>
    private static void InheritRecordKey(CkModelGraph modelGraph, CkRecordGraph recordGraph,
        IEnumerable<CkGraphRecordInheritance> baseRecords)
    {
        foreach (var baseRecord in baseRecords)
        {
            if (modelGraph.Records.TryGetValue(baseRecord.BaseCkRecordId, out var baseGraph))
            {
                recordGraph.InheritRecordKey(baseGraph.RecordKey);
            }
        }
    }

    /// <summary>
    /// Safe version of GetBaseTypes that does not throw on broken inheritance chains.
    /// Returns null if the type's model should be marked as failed.
    /// </summary>
    private static IList<CkGraphTypeInheritance>? GetBaseTypesSafe(CkModelGraph modelGraph,
        CkId<CkTypeId> ckTypeId, IOriginFileResolver originFileResolver, OperationResult operationResult,
        ISet<CkModelId> failedModelIds, HashSet<CkId<CkTypeId>> failedTypeIds)
    {
        var ckTypeIds = new List<CkGraphTypeInheritance>();

        var i = 0;
        CkId<CkTypeId>? currentCkTypeId = ckTypeId;
        CkId<CkTypeId> lastCkTypeId = ckTypeId;
        while (currentCkTypeId != null &&
               modelGraph.Types.TryGetValue(currentCkTypeId, out var currentCkType))
        {
            var baseCkTypeId = currentCkType.DerivedFromCkTypeId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(MessageCodes.DerivedFromCkTypeIdThatIsFinal(
                        originFileResolver.Resolve(ckTypeId),
                        currentCkTypeId, lastCkTypeId));
                    failedModelIds.Add(ckTypeId.ModelId);
                    failedTypeIds.Add(ckTypeId);
                    return null;
                }
            }

            if (baseCkTypeId != null)
            {
                ckTypeIds.Add(new CkGraphTypeInheritance(currentCkTypeId, baseCkTypeId, i++));
            }

            lastCkTypeId = currentCkTypeId;
            currentCkTypeId = baseCkTypeId;
        }

        if (currentCkTypeId != null)
        {
            operationResult.AddMessage(
                MessageCodes.UnknownCkTypeIdForInheritance(originFileResolver.Resolve(ckTypeId), currentCkTypeId));
            failedModelIds.Add(ckTypeId.ModelId);
            failedTypeIds.Add(ckTypeId);
            return null;
        }

        if (!ckTypeIds.Any())
        {
            if (!CompilerStatics.WhiteListedCkTypeIds.Any(x => x.ModelId.Name == ckTypeId.ModelId.Name
                                                               && x.Key.Name == ckTypeId.ElementId.Name))
            {
                operationResult.AddMessage(
                    MessageCodes.InheritanceMissing(originFileResolver.Resolve(ckTypeId), ckTypeId.ElementId.Name));
                failedModelIds.Add(ckTypeId.ModelId);
                failedTypeIds.Add(ckTypeId);
                return null;
            }
        }

        return ckTypeIds;
    }

    /// <summary>
    /// Safe version of GetAndUpdateRecordGraph that does not throw on missing records.
    /// </summary>
    private CkRecordGraph? GetAndUpdateRecordGraphSafe(HashSet<CkId<CkRecordId>> handledRecordHashSet,
        CkModelGraph modelGraph, CkId<CkRecordId> ckRecordId,
        IOriginFileResolver originFileResolver, OperationResult operationResult, ISet<CkModelId> failedModelIds)
    {
        if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
        {
            operationResult.AddMessage(MessageCodes.CkRecordIdUnknown(originFileResolver.Resolve(ckRecordId),
                ckRecordId));
            failedModelIds.Add(ckRecordId.ModelId);
            return null;
        }

        if (!handledRecordHashSet.Contains(ckRecordId))
        {
            var baseTypes = GetBaseRecordsSafe(modelGraph, ckRecordId, originFileResolver, operationResult,
                failedModelIds);
            if (baseTypes == null)
            {
                return null;
            }

            recordGraph.AddBaseRecords(baseTypes);
            InheritRecordKey(modelGraph, recordGraph, baseTypes);

            foreach (var ckTypeAttribute in recordGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtRecord(
                        originFileResolver.Resolve(ckRecordId),
                        ckTypeAttribute.CkAttributeId, ckRecordId));
                    continue;
                }

                recordGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledRecordHashSet.Add(ckRecordId);
        }

        return recordGraph;
    }

    /// <summary>
    /// Safe version of GetBaseRecords that does not throw on broken record inheritance chains.
    /// </summary>
    private static IList<CkGraphRecordInheritance>? GetBaseRecordsSafe(CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, IOriginFileResolver originFileResolver, OperationResult operationResult,
        ISet<CkModelId> failedModelIds)
    {
        var ckRecordIds = new List<CkGraphRecordInheritance>();

        var i = 0;
        CkId<CkRecordId>? currentCkRecordId = ckRecordId;
        CkId<CkRecordId> lastCkRecordId = ckRecordId;
        while (currentCkRecordId != null &&
               modelGraph.Records.TryGetValue(currentCkRecordId, out var currentCkType))
        {
            var baseCkRecordId = currentCkType.DerivedFromCkRecordId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(
                        MessageCodes.DerivedFromCkRecordIdThatIsFinal(originFileResolver.Resolve(ckRecordId),
                            currentCkRecordId, lastCkRecordId));
                    failedModelIds.Add(ckRecordId.ModelId);
                    return null;
                }
            }

            if (baseCkRecordId != null)
            {
                ckRecordIds.Add(new CkGraphRecordInheritance(currentCkRecordId, baseCkRecordId, i++));
            }

            lastCkRecordId = currentCkRecordId;
            currentCkRecordId = baseCkRecordId;
        }

        if (currentCkRecordId != null)
        {
            operationResult.AddMessage(
                MessageCodes.UnknownCkRecordIdForInheritance(originFileResolver.Resolve(ckRecordId),
                    currentCkRecordId));
            failedModelIds.Add(ckRecordId.ModelId);
            return null;
        }

        return ckRecordIds;
    }

    /// <summary>
    /// Safe version of GetDirectedAggregationsAndAttributes that skips types with failed resolution.
    /// </summary>
    private void GetDirectedAggregationsAndAttributesSafe(HashSet<CkId<CkTypeId>> handledTypesHashSet,
        HashSet<CkId<CkTypeId>> failedTypeIds,
        CkModelGraph ckModelGraph, CkTypeGraph originTypeGraph,
        IOriginFileResolver originFileResolver, OperationResult operationResult, ISet<CkModelId> failedModelIds)
    {
        _logger.LogDebug("Resolving directed aggregations and attributes for type {CkTypeId}",
            originTypeGraph.CkTypeId);
        for (var i = originTypeGraph.BaseTypes.Count - 1; i >= 0; i--)
        {
            var ckGraphTypeInheritance = originTypeGraph.BaseTypes.ElementAt(i);
            if (!ckModelGraph.Types.TryGetValue(ckGraphTypeInheritance.BaseCkTypeId, out var baseCkType))
            {
                continue;
            }

            foreach (var typeAttribute in baseCkType.DefinedAttributes)
            {
                if (!baseCkType.AllAttributes.TryGetValue(typeAttribute.CkAttributeId, out var ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(
                        originFileResolver.Resolve(baseCkType.CkTypeId),
                        typeAttribute.CkAttributeId, baseCkType.CkTypeId));
                    continue;
                }

                if (!originTypeGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeIdNotUniqueByInheritance(
                            originFileResolver.Resolve(originTypeGraph.CkTypeId),
                            baseCkType.CkTypeId, typeAttribute.CkAttributeId,
                            originTypeGraph.CkTypeId));
                }
            }
        }

        foreach (var typeAssociation in originTypeGraph.Associations.DefinedAssociations)
        {
            if (!ckModelGraph.AssociationRoles.TryGetValue(typeAssociation.CkRoleId, out var ckAssociationRole))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationRoleIdUnknown(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId));
                continue;
            }

            var targetCkTypeGraph =
                GetAndUpdateTargetCkTypeGraphSafe(handledTypesHashSet, failedTypeIds, ckModelGraph, originTypeGraph,
                    typeAssociation, originFileResolver, operationResult, failedModelIds);
            if (targetCkTypeGraph == null)
            {
                continue;
            }

            if (originTypeGraph.Associations.Out.Owned.Any(x =>
                    x.CkRoleId == typeAssociation.CkRoleId && x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationNotUnique(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                continue;
            }

            var duplicateTypeAssociations = originTypeGraph.BaseTypes.SelectMany(inh =>
            {
                if (!ckModelGraph.Types.TryGetValue(inh.BaseCkTypeId, out var baseCkTypeGraph))
                {
                    return [];
                }

                return baseCkTypeGraph.Associations.Out.Owned.Where(x =>
                        x.CkRoleId == typeAssociation.CkRoleId && originTypeGraph.BaseTypes.Any(y =>
                            y.BaseCkTypeId == x.TargetCkTypeId))
                    .Select(s => new { BaseCkTypeGraph = baseCkTypeGraph, s.TargetCkTypeId });
            }).ToList();

            if (duplicateTypeAssociations.Any())
            {
                foreach (var duplicateTypeAssociation in duplicateTypeAssociations)
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId),
                        originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId,
                        duplicateTypeAssociation.BaseCkTypeGraph.CkTypeId, duplicateTypeAssociation.TargetCkTypeId));
                }

                continue;
            }

            if (typeAssociation.TargetCkAttributeIds != null)
            {
                var invalidCkAttributeIds = typeAssociation.TargetCkAttributeIds.Where(a =>
                    targetCkTypeGraph.AllAttributes.All(b => b.Key != a)).ToList();

                invalidCkAttributeIds.ForEach(a =>
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetAttributeIdForAssociation(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId), originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, a, typeAssociation.TargetCkTypeId));
                });

                if (invalidCkAttributeIds.Any())
                {
                    continue;
                }
            }

            var inboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.InboundName,
                ckAssociationRole.InboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            var outboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.OutboundName,
                ckAssociationRole.OutboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            targetCkTypeGraph.Associations.In.Owned.Add(inboundAssociationGraph);
            originTypeGraph.Associations.Out.Owned.Add(outboundAssociationGraph);
        }

        var duplicateAttributeNames = originTypeGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkTypeIdAttributeNameNotUniqueByInheritance(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId,
                    string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    /// <summary>
    /// Safe version of GetAndUpdateTargetCkTypeGraph that returns null instead of throwing.
    /// </summary>
    private CkTypeGraph? GetAndUpdateTargetCkTypeGraphSafe(HashSet<CkId<CkTypeId>> handledTypesHashSet,
        HashSet<CkId<CkTypeId>> failedTypeIds,
        CkModelGraph ckModelGraph, CkTypeGraph typeGraph,
        CkTypeAssociationDto typeAssociation, IOriginFileResolver originFileResolver,
        OperationResult operationResult, ISet<CkModelId> failedModelIds)
    {
        if (!ckModelGraph.Types.ContainsKey(typeAssociation.TargetCkTypeId))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetCkTypeIdForAssociation(
                originFileResolver.Resolve(typeGraph.CkTypeId),
                typeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
            failedModelIds.Add(typeGraph.CkTypeId.ModelId);
            failedTypeIds.Add(typeGraph.CkTypeId);
            return null;
        }

        return GetAndUpdateTypeGraphSafe(handledTypesHashSet, failedTypeIds, ckModelGraph,
            typeAssociation.TargetCkTypeId, originFileResolver, operationResult, failedModelIds);
    }

    private CkTypeGraph GetAndUpdateTypeGraph(HashSet<CkId<CkTypeId>> handledTypesHashSet, CkModelGraph modelGraph,
        CkId<CkTypeId> ckTypeId,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknown(originFileResolver.Resolve(ckTypeId), ckTypeId));
            throw ModelValidationException.UnknownCkTypeId(ckTypeId);
        }


        if (!handledTypesHashSet.Contains(ckTypeId))
        {
            var baseTypes = GetBaseTypes(modelGraph, ckTypeId, originFileResolver, operationResult);
            typeGraph.AddBaseTypes(baseTypes);

            if (baseTypes.Any() && baseTypes.All(t =>
                    CompilerStatics.WhiteListedCkTypeIds.Any(v => v.IsSatisfiedBy(t.BaseCkTypeId))))
            {
                typeGraph.SetIsCollectionRoot(true);
                typeGraph.SetDefiningCollectionCkTypeId(typeGraph.CkTypeId);
            }

            ResolveDisplayRules(modelGraph, typeGraph, baseTypes);

            foreach (var ckTypeAttribute in typeGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(
                        originFileResolver.Resolve(ckTypeId),
                        ckTypeAttribute.CkAttributeId, ckTypeId));
                    continue;
                }

                typeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledTypesHashSet.Add(ckTypeId);
        }

        return typeGraph;
    }

    private CkRecordGraph GetAndUpdateRecordGraph(HashSet<CkId<CkRecordId>> handledRecordHashSet,
        CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
        {
            operationResult.AddMessage(MessageCodes.CkRecordIdUnknown(originFileResolver.Resolve(ckRecordId),
                ckRecordId));
            throw ModelValidationException.UnknownCkRecordId(ckRecordId);
        }

        if (!handledRecordHashSet.Contains(ckRecordId))
        {
            var baseTypes = GetBaseRecords(modelGraph, ckRecordId, originFileResolver, operationResult);
            recordGraph.AddBaseRecords(baseTypes);
            InheritRecordKey(modelGraph, recordGraph, baseTypes);

            foreach (var ckTypeAttribute in recordGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(ckTypeAttribute.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtRecord(
                        originFileResolver.Resolve(ckRecordId),
                        ckTypeAttribute.CkAttributeId, ckRecordId));
                    continue;
                }

                recordGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    attributeGraph));
            }

            handledRecordHashSet.Add(ckRecordId);
        }

        return recordGraph;
    }

    private void GetDirectedAggregationsAndAttributes(HashSet<CkId<CkTypeId>> handledTypesHashSet,
        CkModelGraph ckModelGraph,
        CkTypeGraph originTypeGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        _logger.LogDebug("Resolving directed aggregations and attributes for type {CkTypeId}",
            originTypeGraph.CkTypeId);
        for (var i = originTypeGraph.BaseTypes.Count - 1; i >= 0; i--)
        {
            var ckGraphTypeInheritance = originTypeGraph.BaseTypes.ElementAt(i);
            var baseCkType = ckModelGraph.Types[ckGraphTypeInheritance.BaseCkTypeId];


            foreach (var typeAttribute in baseCkType.DefinedAttributes)
            {
                if (!baseCkType.AllAttributes.TryGetValue(typeAttribute.CkAttributeId, out var ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdNotFoundAtType(
                        originFileResolver.Resolve(baseCkType.CkTypeId),
                        typeAttribute.CkAttributeId, baseCkType.CkTypeId));
                    continue;
                }

                // Here is checked if the attribute id already exists on the type
                // (e.g. defined at type or inherited from another base type)
                if (!originTypeGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdAttributeIdNotUniqueByInheritance(
                            originFileResolver.Resolve(originTypeGraph.CkTypeId),
                            baseCkType.CkTypeId, typeAttribute.CkAttributeId,
                            originTypeGraph.CkTypeId));
                }
            }
        }

        // Add the current type's associations and attributes
        foreach (var typeAssociation in originTypeGraph.Associations.DefinedAssociations)
        {
            // Check if the association role exists - ckModelGraph already contains all association roles
            if (!ckModelGraph.AssociationRoles.TryGetValue(typeAssociation.CkRoleId, out var ckAssociationRole))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationRoleIdUnknown(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId));
                continue;
            }

            var targetCkTypeGraph =
                GetAndUpdateTargetCkTypeGraph(handledTypesHashSet, ckModelGraph, originTypeGraph, typeAssociation,
                    originFileResolver,
                    operationResult);

            // Check if there is a duplicate association defined at the same type.
            if (originTypeGraph.Associations.Out.Owned.Any(x =>
                    x.CkRoleId == typeAssociation.CkRoleId && x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
            {
                operationResult.AddMessage(MessageCodes.CkTypeIdAssociationNotUnique(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                continue;
            }

            // Check if there is the same association role defined in a base type with a target to the same target type inheritance chain
            var duplicateTypeAssociations = originTypeGraph.BaseTypes.SelectMany(inh =>
            {
                var baseCkTypeGraph = GetAndUpdateTypeGraph(handledTypesHashSet, ckModelGraph, inh.BaseCkTypeId,
                    originFileResolver,
                    operationResult);

                return baseCkTypeGraph.Associations.Out.Owned.Where(x =>
                        x.CkRoleId == typeAssociation.CkRoleId && originTypeGraph.BaseTypes.Any(y =>
                            y.BaseCkTypeId == x.TargetCkTypeId))
                    .Select(s => new { BaseCkTypeGraph = baseCkTypeGraph, s.TargetCkTypeId });
            }).ToList();

            if (duplicateTypeAssociations.Any())
            {
                foreach (var duplicateTypeAssociation in duplicateTypeAssociations)
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdMultipleOutgoingAssociationRepresentingSameRole(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId),
                        originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId,
                        duplicateTypeAssociation.BaseCkTypeGraph.CkTypeId, duplicateTypeAssociation.TargetCkTypeId));
                }

                continue;
            }

            // Check if there are target attributes defined and if they are valid
            if (typeAssociation.TargetCkAttributeIds != null)
            {
                var invalidCkAttributeIds = typeAssociation.TargetCkAttributeIds.Where(a =>
                    targetCkTypeGraph.AllAttributes.All(b => b.Key != a)).ToList();

                invalidCkAttributeIds.ForEach(a =>
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetAttributeIdForAssociation(
                        originFileResolver.Resolve(originTypeGraph.CkTypeId), originTypeGraph.CkTypeId,
                        typeAssociation.CkRoleId, a, typeAssociation.TargetCkTypeId));
                });

                if (invalidCkAttributeIds.Any())
                {
                    continue;
                }
            }

            var inboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.InboundName,
                ckAssociationRole.InboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            var outboundAssociationGraph = new CkTypeAssociationGraph(ckAssociationRole.OutboundName,
                ckAssociationRole.OutboundMultiplicity, originTypeGraph.CkTypeId, typeAssociation);
            targetCkTypeGraph.Associations.In.Owned.Add(inboundAssociationGraph);
            originTypeGraph.Associations.Out.Owned.Add(outboundAssociationGraph);
        }

        // Check if the attributes (=defined+inherited at type) have duplicate attribute names
        var duplicateAttributeNames = originTypeGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkTypeIdAttributeNameNotUniqueByInheritance(
                    originFileResolver.Resolve(originTypeGraph.CkTypeId),
                    originTypeGraph.CkTypeId,
                    string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private void GetDirectedRecordAttributes(CkModelGraph modelGraph,
        CkRecordGraph originRecordGraph, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        for (var i = originRecordGraph.BaseRecords.Count - 1; i >= 0; i--)
        {
            var ckGraphRecordInheritance = originRecordGraph.BaseRecords.ElementAt(i);
            var baseCkRecord = modelGraph.Records[ckGraphRecordInheritance.BaseCkRecordId];

            foreach (var typeAttribute in baseCkRecord.DefinedAttributes)
            {
                // Here is checked if the attribute id already exists on the record
                // (e.g. defined at record or inherited from another base record)
                var ckTypeAttributeGraph = baseCkRecord.AllAttributes[typeAttribute.CkAttributeId];
                if (!originRecordGraph.TryAddAttribute(ckTypeAttributeGraph))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkRecordIdAttributeIdNotUniqueByInheritance(
                            originFileResolver.Resolve(baseCkRecord.CkRecordId),
                            baseCkRecord.CkRecordId, typeAttribute.CkAttributeId, originRecordGraph.CkRecordId));
                }
            }
        }

        // Check if the attributes (=defined+inherited at record) have duplicate attribute names
        var duplicateAttributeNames = originRecordGraph.AllAttributes.Values.GroupBy(a => a.AttributeName)
            .Where(a => a.Count() > 1).ToList();
        if (duplicateAttributeNames.Count > 0)
        {
            operationResult.AddMessage(
                MessageCodes.CkRecordIdAttributeNameNotUniqueByInheritance(
                    originFileResolver.Resolve(originRecordGraph.CkRecordId),
                    originRecordGraph.CkRecordId,
                    string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
        }
    }

    private CkTypeGraph GetAndUpdateTargetCkTypeGraph(HashSet<CkId<CkTypeId>> handledTypesHashSet,
        CkModelGraph ckModelGraph,
        CkTypeGraph typeGraph,
        CkTypeAssociationDto typeAssociation, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        if (!ckModelGraph.Types.ContainsKey(typeAssociation.TargetCkTypeId))
        {
            operationResult.AddMessage(MessageCodes.CkTypeIdUnknownTargetCkTypeIdForAssociation(
                originFileResolver.Resolve(typeGraph.CkTypeId),
                typeGraph.CkTypeId, typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForAssociationTarget(typeGraph.CkTypeId,
                typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId);
        }

        var targetCkTypeGraph = GetAndUpdateTypeGraph(handledTypesHashSet, ckModelGraph, typeAssociation.TargetCkTypeId,
            originFileResolver,
            operationResult);
        return targetCkTypeGraph;
    }

    private void BuildInheritedConfiguration(CkModelGraph modelGraph, HashSet<CkId<CkTypeId>> failedTypeIds,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var handledInheritanceHashSet = new HashSet<Tuple<CkId<CkTypeId>, CkId<CkTypeId>>>();
        foreach (var graphType in modelGraph.Types)
        {
            // Skip types that failed inheritance resolution
            if (failedTypeIds.Contains(graphType.Key))
            {
                continue;
            }

            List<CkTypeGraph> baseList = [];
            foreach (var ckGraphTypeInheritance in graphType.Value.BaseTypes.Reverse())
            {
                if (!modelGraph.Types.TryGetValue(ckGraphTypeInheritance.BaseCkTypeId, out var baseGraphType))
                {
                    continue;
                }

                if (!modelGraph.Types.TryGetValue(ckGraphTypeInheritance.InheritorCkTypeId,
                        out var inheritedGraphType))
                {
                    continue;
                }

                baseList.Add(baseGraphType);

                // Set the defining collection type id and merge index fields.
                if (baseGraphType.IsCollectionRoot)
                {
                    graphType.Value.SetDefiningCollectionCkTypeId(baseGraphType.CkTypeId);

                    baseGraphType.MergeTextIndexes(graphType.Value.Indexes);
                }

                // Ensure that we don't handle the same inheritance twice
                var tuple = new Tuple<CkId<CkTypeId>, CkId<CkTypeId>>(baseGraphType.CkTypeId,
                    inheritedGraphType.CkTypeId);
                // ReSharper disable once CanSimplifySetAddingWithSingleCall
                if (handledInheritanceHashSet.Contains(tuple))
                {
                    continue;
                }

                handledInheritanceHashSet.Add(tuple);
                baseList.ForEach(b => b.AddDerivedTypes(ckGraphTypeInheritance));

                // Add the owned associations but also the inherited ones
                foreach (var typeAssociation in baseGraphType.Associations.In.Owned)
                {
                    inheritedGraphType.Associations.In.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.In.Inherited)
                {
                    inheritedGraphType.Associations.In.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.Out.Owned)
                {
                    if (inheritedGraphType.Associations.Out.Inherited.Any(x =>
                            x.CkRoleId == typeAssociation.CkRoleId &&
                            x.TargetCkTypeId == typeAssociation.TargetCkTypeId))
                    {
                        operationResult.AddMessage(MessageCodes.CkTypeIdOutAssociationNotUniqueByInheritance(
                            originFileResolver.Resolve(inheritedGraphType.CkTypeId),
                            inheritedGraphType.CkTypeId,
                            typeAssociation.CkRoleId, typeAssociation.TargetCkTypeId));
                        continue;
                    }

                    inheritedGraphType.Associations.Out.Inherited.Add(typeAssociation);
                }

                foreach (var typeAssociation in baseGraphType.Associations.Out.Inherited)
                {
                    inheritedGraphType.Associations.Out.Inherited.Add(typeAssociation);
                }
            }
        }
    }

    private static IList<CkGraphTypeInheritance> GetBaseTypes(CkModelGraph modelGraph,
        CkId<CkTypeId> ckTypeId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var ckTypeIds = new List<CkGraphTypeInheritance>();

        var i = 0;
        CkId<CkTypeId>? currentCkTypeId = ckTypeId;
        CkId<CkTypeId> lastCkTypeId = ckTypeId;
        while (currentCkTypeId != null &&
               modelGraph.Types.TryGetValue(currentCkTypeId, out var currentCkType))
        {
            var baseCkTypeId = currentCkType.DerivedFromCkTypeId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(MessageCodes.DerivedFromCkTypeIdThatIsFinal(
                        originFileResolver.Resolve(ckTypeId),
                        currentCkTypeId, lastCkTypeId));
                    throw ModelValidationException.DerivedFromCkTypeIdThatIsFinal(currentCkTypeId, lastCkTypeId);
                }
            }

            if (baseCkTypeId != null)
            {
                ckTypeIds.Add(new CkGraphTypeInheritance(currentCkTypeId, baseCkTypeId, i++));
            }

            lastCkTypeId = currentCkTypeId;
            currentCkTypeId = baseCkTypeId;
        }

        if (currentCkTypeId != null)
        {
            operationResult.AddMessage(
                MessageCodes.UnknownCkTypeIdForInheritance(originFileResolver.Resolve(ckTypeId), currentCkTypeId));
            throw ModelValidationException.UnknownCkTypeIdForInheritance(currentCkTypeId);
        }

        if (!ckTypeIds.Any())
        {
            if (!CompilerStatics.WhiteListedCkTypeIds.Any(x => x.ModelId.Name == ckTypeId.ModelId.Name
                                                               && x.Key.Name == ckTypeId.ElementId.Name))
            {
                operationResult.AddMessage(
                    MessageCodes.InheritanceMissing(originFileResolver.Resolve(ckTypeId), ckTypeId.ElementId.Name));
                throw ModelValidationException.InheritanceMissing(ckTypeId.ElementId.Name);
            }
        }

        return ckTypeIds;
    }

    private static IList<CkGraphRecordInheritance> GetBaseRecords(CkModelGraph modelGraph,
        CkId<CkRecordId> ckRecordId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var ckRecordIds = new List<CkGraphRecordInheritance>();

        var i = 0;
        CkId<CkRecordId>? currentCkRecordId = ckRecordId;
        CkId<CkRecordId> lastCkRecordId = ckRecordId;
        while (currentCkRecordId != null &&
               modelGraph.Records.TryGetValue(currentCkRecordId, out var currentCkType))
        {
            var baseCkRecordId = currentCkType.DerivedFromCkRecordId;

            if (i != 0)
            {
                if (currentCkType.IsFinal)
                {
                    operationResult.AddMessage(
                        MessageCodes.DerivedFromCkRecordIdThatIsFinal(originFileResolver.Resolve(ckRecordId),
                            currentCkRecordId, lastCkRecordId));
                    throw ModelValidationException.DerivedFromCkRecordIdThatIsFinal(currentCkRecordId, lastCkRecordId);
                }
            }

            if (baseCkRecordId != null)
            {
                ckRecordIds.Add(new CkGraphRecordInheritance(currentCkRecordId, baseCkRecordId, i++));
            }

            lastCkRecordId = currentCkRecordId;
            currentCkRecordId = baseCkRecordId;
        }

        if (currentCkRecordId != null)
        {
            operationResult.AddMessage(
                MessageCodes.UnknownCkRecordIdForInheritance(originFileResolver.Resolve(ckRecordId),
                    currentCkRecordId));
            throw ModelValidationException.UnknownCkRecordIdForInheritance(currentCkRecordId);
        }

        return ckRecordIds;
    }
}
