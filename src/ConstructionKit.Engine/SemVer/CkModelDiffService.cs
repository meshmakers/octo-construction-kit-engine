using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkModelDiffService" />.
/// </summary>
/// <remarks>
///     Reference comparison semantics: references to elements of the model itself are compared
///     ignoring the model version (a version bump of the model must not turn every
///     <c>${this}</c> reference into a change), while references into other models are compared
///     by name and major version (<c>SemanticVersionedFullName</c>) — a dependency switching to a
///     new major version therefore surfaces as a reference change, whereas minor/revision bumps
///     of dependencies do not produce reference noise (they surface in the dependency diff).
/// </remarks>
public class CkModelDiffService : ICkModelDiffService
{
    /// <summary>
    ///     DTO properties this diff compares, per DTO type. Together with <see cref="ExcludedProperties" /> it
    ///     must cover every public property of the element DTOs; the classification guard test
    ///     (<c>CkSemVerClassificationGuardTests</c>) fails otherwise. Every compared property needs a probe in
    ///     that guard test that proves the diff emits a change for it, and every emitted change must reach an
    ///     explicit classifier rule (AB#6272). Identity keys (element ids, member names) are compared as keys:
    ///     changing one surfaces as remove + add.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, IReadOnlyCollection<string>> ComparedProperties =
        new Dictionary<Type, IReadOnlyCollection<string>>
        {
            [typeof(CkCompiledModelRoot)] = [nameof(CkCompiledModelRoot.Dependencies), nameof(CkCompiledModelRoot.DependencyRanges)],
            // CK v2 range retention (AB#6271)
            [typeof(CkModelDependencyDto)] = [nameof(CkModelDependencyDto.Range), nameof(CkModelDependencyDto.Floor)],
            [typeof(CkModelRootBase)] =
            [
                nameof(CkModelRootBase.Types), nameof(CkModelRootBase.AssociationRoles), nameof(CkModelRootBase.Attributes),
                nameof(CkModelRootBase.Records), nameof(CkModelRootBase.Enums), nameof(CkModelRootBase.Interfaces)
            ],
            [typeof(CkModelPropertiesDto)] = [nameof(CkModelPropertiesDto.Description), nameof(CkModelPropertiesDto.CkLanguage)],
            [typeof(CkCompiledTypeDto)] = [nameof(CkCompiledTypeDto.IsCollectionRoot)],
            [typeof(CkTypeDto)] =
            [
                nameof(CkTypeDto.TypeId), nameof(CkTypeDto.DerivedFromCkTypeId), nameof(CkTypeDto.IsFinal),
                nameof(CkTypeDto.IsAbstract), nameof(CkTypeDto.Indexes), nameof(CkTypeDto.Associations),
                nameof(CkTypeDto.EnableChangeStreamPreAndPostImages), nameof(CkTypeDto.Description),
                nameof(CkTypeDto.DisplayNameRule), nameof(CkTypeDto.DisplayDescriptionRule),
                nameof(CkTypeDto.OwnerAttributePath), nameof(CkTypeDto.Implements), nameof(CkTypeDto.Methods),
                nameof(CkTypeDto.Visibility), nameof(CkTypeDto.Derivable)
            ],
            [typeof(CkTypeWithAttributesDto)] = [nameof(CkTypeWithAttributesDto.Attributes)],
            [typeof(CkAttributeDto)] =
            [
                nameof(CkAttributeDto.AttributeId), nameof(CkAttributeDto.ValueType), nameof(CkAttributeDto.ValueCkRecordId),
                nameof(CkAttributeDto.ValueCkEnumId), nameof(CkAttributeDto.DefaultValues), nameof(CkAttributeDto.IsRuntimeState),
                nameof(CkAttributeDto.Ownership), nameof(CkAttributeDto.Description), nameof(CkAttributeDto.MetaData),
                nameof(CkAttributeDto.Visibility), nameof(CkAttributeDto.SecuritySensitive)
            ],
            [typeof(CkEnumDto)] =
            [
                nameof(CkEnumDto.EnumId), nameof(CkEnumDto.UseFlags), nameof(CkEnumDto.IsExtensible),
                nameof(CkEnumDto.Values), nameof(CkEnumDto.Description), nameof(CkEnumDto.Visibility)
            ],
            [typeof(CkEnumValueDto)] =
            [
                nameof(CkEnumValueDto.Key), nameof(CkEnumValueDto.Name), nameof(CkEnumValueDto.Description),
                nameof(CkEnumValueDto.IsExtension)
            ],
            [typeof(CkRecordDto)] =
            [
                nameof(CkRecordDto.RecordId), nameof(CkRecordDto.DerivedFromCkRecordId), nameof(CkRecordDto.IsFinal),
                nameof(CkRecordDto.IsAbstract), nameof(CkRecordDto.Description), nameof(CkRecordDto.RecordKey),
                nameof(CkRecordDto.Visibility), nameof(CkRecordDto.Derivable)
            ],
            [typeof(CkAssociationRoleDto)] =
            [
                nameof(CkAssociationRoleDto.AssociationRoleId), nameof(CkAssociationRoleDto.InboundName),
                nameof(CkAssociationRoleDto.OutboundName), nameof(CkAssociationRoleDto.InboundMultiplicity),
                nameof(CkAssociationRoleDto.OutboundMultiplicity), nameof(CkAssociationRoleDto.Description),
                nameof(CkAssociationRoleDto.Visibility)
            ],
            [typeof(CkTypeAttributeDto)] =
            [
                nameof(CkTypeAttributeDto.CkAttributeId), nameof(CkTypeAttributeDto.AttributeName),
                nameof(CkTypeAttributeDto.AutoCompleteValues), nameof(CkTypeAttributeDto.AutoIncrementReference),
                nameof(CkTypeAttributeDto.IsOptional), nameof(CkTypeAttributeDto.Ownership), nameof(CkTypeAttributeDto.Access)
            ],
            [typeof(CkTypeAssociationDto)] =
            [
                nameof(CkTypeAssociationDto.CkRoleId), nameof(CkTypeAssociationDto.TargetCkTypeId),
                nameof(CkTypeAssociationDto.TargetCkAttributeIds), nameof(CkTypeAssociationDto.TargetCkInterfaceId)
            ],
            [typeof(CkTypeIndexDto)] = [nameof(CkTypeIndexDto.IndexType), nameof(CkTypeIndexDto.Language), nameof(CkTypeIndexDto.Fields)],
            [typeof(CkIndexFieldsDto)] = [nameof(CkIndexFieldsDto.Weight), nameof(CkIndexFieldsDto.AttributePaths)],
            [typeof(CkAttributeMetaDataDto)] =
                [nameof(CkAttributeMetaDataDto.Key), nameof(CkAttributeMetaDataDto.Value), nameof(CkAttributeMetaDataDto.Description)],
            // CK v2 (AB#5667 / AB#5669)
            [typeof(CkInterfaceDto)] =
            [
                nameof(CkInterfaceDto.InterfaceId), nameof(CkInterfaceDto.Description), nameof(CkInterfaceDto.Attributes),
                nameof(CkInterfaceDto.Visibility), nameof(CkInterfaceDto.Extends), nameof(CkInterfaceDto.Associations),
                nameof(CkInterfaceDto.Methods), nameof(CkInterfaceDto.Deprecated)
            ],
            // CK v2 (F1.1-S5)
            [typeof(CkInterfaceAssociationDto)] =
            [
                nameof(CkInterfaceAssociationDto.CkRoleId), nameof(CkInterfaceAssociationDto.TargetCkTypeId),
                nameof(CkInterfaceAssociationDto.TargetCkInterfaceId), nameof(CkInterfaceAssociationDto.Multiplicity),
                nameof(CkInterfaceAssociationDto.IsOptional)
            ],
            [typeof(CkInterfaceAttributeDto)] =
            [
                nameof(CkInterfaceAttributeDto.CkAttributeId), nameof(CkInterfaceAttributeDto.AttributeName),
                nameof(CkInterfaceAttributeDto.IsOptional)
            ],
            [typeof(CkMethodDto)] =
            [
                nameof(CkMethodDto.MethodId), nameof(CkMethodDto.Kind), nameof(CkMethodDto.Description),
                nameof(CkMethodDto.Parameters), nameof(CkMethodDto.Result), nameof(CkMethodDto.Errors),
                nameof(CkMethodDto.Authorization), nameof(CkMethodDto.Execution), nameof(CkMethodDto.Visibility)
            ],
            [typeof(CkMethodParameterDto)] =
            [
                nameof(CkMethodParameterDto.Name), nameof(CkMethodParameterDto.ValueType),
                nameof(CkMethodParameterDto.ValueCkRecordId), nameof(CkMethodParameterDto.ValueCkEnumId),
                nameof(CkMethodParameterDto.IsOptional), nameof(CkMethodParameterDto.Sensitive),
                nameof(CkMethodParameterDto.Description)
            ],
            [typeof(CkMethodResultDto)] =
            [
                nameof(CkMethodResultDto.ValueType), nameof(CkMethodResultDto.ValueCkRecordId),
                nameof(CkMethodResultDto.ValueCkEnumId)
            ],
            [typeof(CkMethodErrorDto)] = [nameof(CkMethodErrorDto.Code), nameof(CkMethodErrorDto.Description)],
            [typeof(CkMethodAuthorizationDto)] =
            [
                nameof(CkMethodAuthorizationDto.Roles), nameof(CkMethodAuthorizationDto.AllowSelf),
                nameof(CkMethodAuthorizationDto.Scopes)
            ],
            [typeof(CkMethodExecutionDto)] = [nameof(CkMethodExecutionDto.TimeoutSeconds), nameof(CkMethodExecutionDto.Idempotent)]
        };

    /// <summary>
    ///     DTO properties the diff consciously does not compare, per DTO type, each with the written reason
    ///     (AB#6272: an exclusion is a decision, not a code comment). The classification guard test fails for an
    ///     exclusion without a reason and for a property that is both compared and excluded.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, IReadOnlyDictionary<string, string>> ExcludedProperties =
        new Dictionary<Type, IReadOnlyDictionary<string, string>>
        {
            [typeof(CkCompiledModelRoot)] = new Dictionary<string, string>
            {
                [nameof(CkCompiledModelRoot.SchemaUri)] = "serialization constant, identical for every compiled model",
                [nameof(CkCompiledModelRoot.Migrations)] =
                    "migrations always accompany a version bump by design; ValidateVersion reconciles them separately " +
                    "(migration check, OCTO-CK104)",
                [nameof(CkCompiledModelRoot.IsRangeRetaining)] =
                    "computed from DependencyRanges (DependencyRanges != null); diffed as the model change 'rangeRetention' (AB#6271)",
                [nameof(CkCompiledModelRoot.MinEngineVersion)] =
                    "derived by the compiler from ckLanguage, range retention and the dependencies' minEngineVersion " +
                    "(F1.1-S6); its causes are diffed"
            },
            [typeof(CkModelDependencyDto)] = new Dictionary<string, string>
            {
                [nameof(CkModelDependencyDto.FloorVersion)] = "computed view of Floor (not serialized)",
                // Row D7 (AB#4472): derived from the model's own references, which are classified elsewhere.
                [nameof(CkModelDependencyDto.UsedSurface)] =
                    "derived from the model's own references, which are diffed and classified elsewhere (row D7)",
                [nameof(CkModelDependencyDto.UsedSurfaceHash)] = "hash of UsedSurface (row D7)"
            },
            [typeof(CkModelPropertiesDto)] = new Dictionary<string, string>
            {
                [nameof(CkModelPropertiesDto.ModelId)] =
                    "the model identity: the name is equal by construction and the version is what ValidateVersion " +
                    "checks against the diff",
                [nameof(CkModelPropertiesDto.EffectiveCkLanguage)] =
                    "computed view of CkLanguage (not serialized); the diff compares ckLanguage on this effective value"
            }
        };

    /// <summary>
    ///     Registry of all DTO properties this diff accounts for: the union of <see cref="ComparedProperties" />
    ///     and <see cref="ExcludedProperties" />. Kept for consumers of the Phase 1 registry.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, IReadOnlyCollection<string>> AccountedProperties =
        ComparedProperties.Keys.Union(ExcludedProperties.Keys).ToDictionary(type => type,
            type => (IReadOnlyCollection<string>)(ComparedProperties.TryGetValue(type, out var compared) ? compared : [])
                .Concat(ExcludedProperties.TryGetValue(type, out var excluded) ? excluded.Keys : [])
                .ToList());

    /// <summary>
    ///     Every change shape this diff can emit (AB#6272). The classification guard test classifies one
    ///     synthetic change per shape and fails when it reaches the classifier's defensive default, and its probes
    ///     fail when the diff emits a shape that is missing here. Extend this list together with the diff.
    /// </summary>
    public static readonly IReadOnlyList<CkModelChangeShape> EmittableChanges = BuildEmittableChanges();

    private static List<CkModelChangeShape> BuildEmittableChanges()
    {
        var shapes = new List<CkModelChangeShape>();

        void Element(CkModelElementKind kind, params string[] modifiedProperties)
        {
            shapes.Add(new CkModelChangeShape(kind, CkModelChangeKind.Added));
            shapes.Add(new CkModelChangeShape(kind, CkModelChangeKind.Removed));
            Modified(kind, modifiedProperties);
        }

        void Modified(CkModelElementKind kind, params string[] properties) =>
            shapes.AddRange(properties.Select(p => new CkModelChangeShape(kind, CkModelChangeKind.Modified, p)));

        Modified(CkModelElementKind.Model, "description", "ckLanguage", "rangeRetention");
        Element(CkModelElementKind.Dependency, "version");
        Element(CkModelElementKind.Type, "derivedFromCkTypeId", "isFinal", "isAbstract", "isCollectionRoot",
            "enableChangeStreamPreAndPostImages", "description", "displayNameRule", "displayDescriptionRule",
            "ownerAttributePath", "visibility", "derivable");
        foreach (var assignmentKind in new[]
                 {
                     CkModelElementKind.TypeAttribute, CkModelElementKind.RecordAttribute,
                     CkModelElementKind.AssociationRoleAttribute
                 })
        {
            Element(assignmentKind, "id", "isOptional", "autoCompleteValues", "autoIncrementReference", "ownership",
                "access");
        }

        Element(CkModelElementKind.TypeAssociation, "targetCkAttributeIds", "targetCkInterfaceId");
        Element(CkModelElementKind.TypeIndex);
        Element(CkModelElementKind.TypeInterface);
        string[] methodProperties =
        [
            "description", "signature", "visibility", "kind", "result", "timeoutSeconds", "idempotent", "authorization",
            "roles", "scopes", "allowSelf"
        ];
        Element(CkModelElementKind.TypeMethod, methodProperties);
        Element(CkModelElementKind.Attribute, "valueType", "valueCkRecordId", "valueCkEnumId", "defaultValues",
            "isRuntimeState", "ownership", "metaData", "description", "visibility", "securitySensitive");
        Element(CkModelElementKind.Enum, "useFlags", "isExtensible", "description", "visibility");
        Element(CkModelElementKind.EnumValue, "key", "isExtension", "description");
        Element(CkModelElementKind.Record, "derivedFromCkRecordId", "isFinal", "isAbstract", "recordKey", "description",
            "visibility", "derivable");
        Element(CkModelElementKind.AssociationRole, "inboundName", "outboundName", "inboundMultiplicity",
            "outboundMultiplicity", "description", "visibility");
        Element(CkModelElementKind.Interface, "description", "deprecated", "visibility");
        Element(CkModelElementKind.InterfaceAttribute, "id", "isOptional");
        Element(CkModelElementKind.InterfaceExtends);
        Element(CkModelElementKind.InterfaceAssociation, "target", "multiplicity", "isOptional");
        Element(CkModelElementKind.InterfaceMethod, methodProperties);
        Element(CkModelElementKind.MethodParameter, "valueType", "valueCkRecordId", "valueCkEnumId", "isOptional",
            "sensitive", "description");
        Element(CkModelElementKind.MethodError, "description");
        Element(CkModelElementKind.DependencyRange, "range", "floor");
        return shapes;
    }

    /// <inheritdoc />
    public IReadOnlyList<CkModelChange> Diff(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = new List<CkModelChange>();
        var modelName = current.ModelId.Name;

        AddModified(changes, CkModelElementKind.Model, modelName, "description", baseline.Description, current.Description);
        // CK v2 (AB#5584): compared on the effective value, so an omitted key and 'ckLanguage: 1' are equal.
        AddModified(changes, CkModelElementKind.Model, modelName, "ckLanguage",
            baseline.EffectiveCkLanguage.ToString(CultureInfo.InvariantCulture),
            current.EffectiveCkLanguage.ToString(CultureInfo.InvariantCulture));

        DiffDependencies(changes, baseline.Dependencies, current.Dependencies);
        DiffDependencyRanges(changes, baseline, current, modelName);
        DiffTypes(changes, baseline.Types, current.Types, modelName);
        DiffAttributes(changes, baseline.Attributes, current.Attributes, modelName);
        DiffEnums(changes, baseline.Enums, current.Enums);
        DiffRecords(changes, baseline.Records, current.Records, modelName);
        DiffAssociationRoles(changes, baseline.AssociationRoles, current.AssociationRoles, modelName);
        DiffInterfaces(changes, baseline.Interfaces, current.Interfaces, modelName);
        DiffModifiers(changes, baseline, current);

        return changes;
    }

    /// <summary>
    ///     CK v2 (F1.1-S4): effective <c>visibility</c> / <c>derivable</c> of elements present in both versions. The
    ///     derivable default depends on the model's CK language (v1 Any, v2 Model), so adopting <c>ckLanguage: 2</c>
    ///     without declaring <c>derivable: Any</c> shows up as a derivable change.
    /// </summary>
    private static void DiffModifiers(List<CkModelChange> changes, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current)
    {
        var baselineLanguage = baseline.EffectiveCkLanguage;
        var currentLanguage = current.EffectiveCkLanguage;

        void Compare<T>(CkModelElementKind kind, IEnumerable<T>? b, IEnumerable<T>? c, Func<T, string> id,
            Func<T, CkVisibilityDto?> visibility, Func<T, CkDerivableDto?>? derivable = null)
        {
            var before = (b ?? []).ToDictionary(id);
            foreach (var element in c ?? [])
            {
                if (!before.TryGetValue(id(element), out var old))
                {
                    continue;
                }

                AddModified(changes, kind, id(element), "visibility",
                    CkModifiers.ResolveVisibility(visibility(old)).ToString(),
                    CkModifiers.ResolveVisibility(visibility(element)).ToString());
                if (derivable != null)
                {
                    AddModified(changes, kind, id(element), "derivable",
                        CkModifiers.ResolveDerivable(derivable(old), baselineLanguage).ToString(),
                        CkModifiers.ResolveDerivable(derivable(element), currentLanguage).ToString());
                }
            }
        }

        Compare(CkModelElementKind.Type, baseline.Types, current.Types, t => t.TypeId.FullName, t => t.Visibility,
            t => t.Derivable);
        Compare(CkModelElementKind.Record, baseline.Records, current.Records, r => r.RecordId.FullName,
            r => r.Visibility, r => r.Derivable);
        Compare(CkModelElementKind.Enum, baseline.Enums, current.Enums, e => e.EnumId.FullName, e => e.Visibility);
        Compare(CkModelElementKind.Attribute, baseline.Attributes, current.Attributes, a => a.AttributeId.FullName,
            a => a.Visibility);
        Compare(CkModelElementKind.AssociationRole, baseline.AssociationRoles, current.AssociationRoles,
            r => r.AssociationRoleId.FullName, r => r.Visibility);
        Compare(CkModelElementKind.Interface, baseline.Interfaces, current.Interfaces, i => i.InterfaceId.FullName,
            i => i.Visibility);
        Compare(CkModelElementKind.TypeMethod,
            (baseline.Types ?? []).SelectMany(t => (t.Methods ?? []).Select(m => (Type: t, Method: m))),
            (current.Types ?? []).SelectMany(t => (t.Methods ?? []).Select(m => (Type: t, Method: m))),
            x => $"{x.Type.TypeId.FullName}/{x.Method.MethodId}", x => x.Method.Visibility);
        Compare(CkModelElementKind.InterfaceMethod,
            (baseline.Interfaces ?? []).SelectMany(i => (i.Methods ?? []).Select(m => (Interface: i, Method: m))),
            (current.Interfaces ?? []).SelectMany(i => (i.Methods ?? []).Select(m => (Interface: i, Method: m))),
            x => $"{x.Interface.InterfaceId.FullName}/{x.Method.MethodId}", x => x.Method.Visibility);
    }

    /// <summary>
    ///     CK v2 (AB#6271): declared ranges and floors of a range-retaining model. Switching between exact pins and
    ///     range retention is one model-level <c>rangeRetention</c> change (the entries are not compared then); otherwise
    ///     every declared range is compared by dependency name. The exact closure (<c>Dependencies</c>) is still diffed
    ///     as before ("resolved dependency changed" stays a rule until F2.4).
    /// </summary>
    private static void DiffDependencyRanges(List<CkModelChange> changes, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current, string modelName)
    {
        AddModified(changes, CkModelElementKind.Model, modelName, "rangeRetention",
            baseline.IsRangeRetaining, current.IsRangeRetaining);
        if (!baseline.IsRangeRetaining || !current.IsRangeRetaining)
        {
            return;
        }

        DiffElements(changes, CkModelElementKind.DependencyRange, baseline.DependencyRanges, current.DependencyRanges,
            d => d.Range.Name,
            (rangeChanges, id, baselineRange, currentRange) =>
            {
                AddModified(rangeChanges, CkModelElementKind.DependencyRange, id, "range",
                    baselineRange.Range.ModelVersionRange.ToString(), currentRange.Range.ModelVersionRange.ToString());
                AddModified(rangeChanges, CkModelElementKind.DependencyRange, id, "floor",
                    baselineRange.Floor, currentRange.Floor);
            },
            added => $"{added.Range.FullName}, floor {added.Floor}",
            removed => $"{removed.Range.FullName}, floor {removed.Floor}");
    }

    private static void DiffDependencies(List<CkModelChange> changes, List<CkModelId>? baseline, List<CkModelId>? current)
    {
        var baselineByName = (baseline ?? []).ToDictionary(d => d.Name);
        var currentByName = (current ?? []).ToDictionary(d => d.Name);

        foreach (var pair in currentByName.Where(pair => !baselineByName.ContainsKey(pair.Key)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Added, ElementKind = CkModelElementKind.Dependency,
                ElementId = pair.Key, NewValue = pair.Value.FullName
            });
        }

        foreach (var pair in baselineByName.Where(pair => !currentByName.ContainsKey(pair.Key)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Removed, ElementKind = CkModelElementKind.Dependency,
                ElementId = pair.Key, OldValue = pair.Value.FullName
            });
        }

        foreach (var pair in currentByName)
        {
            if (!baselineByName.TryGetValue(pair.Key, out var baselineDependency))
            {
                continue;
            }

            AddModified(changes, CkModelElementKind.Dependency, pair.Key, "version",
                baselineDependency.Version.ToString(), pair.Value.Version.ToString());
        }
    }

    private static void DiffTypes(List<CkModelChange> changes, List<CkCompiledTypeDto>? baseline,
        List<CkCompiledTypeDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.Type, baseline, current, t => t.TypeId.FullName,
            (typeChanges, id, baselineType, currentType) =>
            {
                AddModified(typeChanges, CkModelElementKind.Type, id, "derivedFromCkTypeId",
                    FormatReference(baselineType.DerivedFromCkTypeId, modelName),
                    FormatReference(currentType.DerivedFromCkTypeId, modelName));
                AddModified(typeChanges, CkModelElementKind.Type, id, "isFinal", baselineType.IsFinal, currentType.IsFinal);
                AddModified(typeChanges, CkModelElementKind.Type, id, "isAbstract", baselineType.IsAbstract, currentType.IsAbstract);
                AddModified(typeChanges, CkModelElementKind.Type, id, "isCollectionRoot",
                    baselineType.IsCollectionRoot, currentType.IsCollectionRoot);
                AddModified(typeChanges, CkModelElementKind.Type, id, "enableChangeStreamPreAndPostImages",
                    baselineType.EnableChangeStreamPreAndPostImages, currentType.EnableChangeStreamPreAndPostImages);
                AddModified(typeChanges, CkModelElementKind.Type, id, "description",
                    baselineType.Description, currentType.Description);
                AddModified(typeChanges, CkModelElementKind.Type, id, "displayNameRule",
                    baselineType.DisplayNameRule, currentType.DisplayNameRule);
                AddModified(typeChanges, CkModelElementKind.Type, id, "displayDescriptionRule",
                    baselineType.DisplayDescriptionRule, currentType.DisplayDescriptionRule);
                AddModified(typeChanges, CkModelElementKind.Type, id, "ownerAttributePath",
                    baselineType.OwnerAttributePath, currentType.OwnerAttributePath);

                DiffAttributeAssignments(typeChanges, CkModelElementKind.TypeAttribute, id,
                    baselineType.Attributes, currentType.Attributes, modelName);
                DiffTypeAssociations(typeChanges, id, baselineType.Associations, currentType.Associations, modelName);
                DiffTypeIndexes(typeChanges, id, baselineType.Indexes, currentType.Indexes);
                DiffTypeInterfaces(typeChanges, id, baselineType.Implements, currentType.Implements, modelName);
                DiffTypeMethods(typeChanges, id, baselineType.Methods, currentType.Methods, modelName);
            });
    }

    /// <summary>
    ///     CK v2 (AB#5667): interface definitions and their members. Members are keyed by member name.
    /// </summary>
    private static void DiffInterfaces(List<CkModelChange> changes, List<CkInterfaceDto>? baseline,
        List<CkInterfaceDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.Interface, baseline, current, i => i.InterfaceId.FullName,
            (interfaceChanges, id, baselineInterface, currentInterface) =>
            {
                AddModified(interfaceChanges, CkModelElementKind.Interface, id, "description",
                    baselineInterface.Description, currentInterface.Description);
                DiffElements(interfaceChanges, CkModelElementKind.InterfaceAttribute, baselineInterface.Attributes,
                    currentInterface.Attributes, a => $"{id}/{a.AttributeName}",
                    (memberChanges, memberId, baselineMember, currentMember) =>
                    {
                        AddModified(memberChanges, CkModelElementKind.InterfaceAttribute, memberId, "id",
                            FormatReference(baselineMember.CkAttributeId, modelName),
                            FormatReference(currentMember.CkAttributeId, modelName));
                        AddModified(memberChanges, CkModelElementKind.InterfaceAttribute, memberId, "isOptional",
                            baselineMember.IsOptional, currentMember.IsOptional);
                    },
                    added => $"{FormatReference(added.CkAttributeId, modelName)}, {(added.IsOptional ? "optional" : "required")}",
                    removed => $"{FormatReference(removed.CkAttributeId, modelName)}, {(removed.IsOptional ? "optional" : "required")}");

                // F1.1-S5: deprecated flag, extends, association and method members.
                AddModified(interfaceChanges, CkModelElementKind.Interface, id, "deprecated",
                    baselineInterface.Deprecated ?? false, currentInterface.Deprecated ?? false);
                DiffElements(interfaceChanges, CkModelElementKind.InterfaceExtends,
                    baselineInterface.Extends?.Select(i => FormatReference(i, modelName)!).Distinct().ToList(),
                    currentInterface.Extends?.Select(i => FormatReference(i, modelName)!).Distinct().ToList(),
                    reference => $"{id}/{reference}", (_, _, _, _) => { });

                // AB#6267 (row I6): an association member is keyed by its role, so a target change is one "target"
                // modification instead of remove + add. When a role occurs twice in one interface (either version),
                // the target stays part of the key for that interface.
                string Target(CkInterfaceAssociationDto a) =>
                    (FormatReference(a.TargetCkTypeId, modelName) ?? FormatReference(a.TargetCkInterfaceId, modelName))!;
                var keyIncludesTarget = HasDuplicateRole(baselineInterface.Associations) ||
                                        HasDuplicateRole(currentInterface.Associations);

                string AssociationKey(CkInterfaceAssociationDto a) =>
                    $"{id}/{FormatReference(a.CkRoleId, modelName)}" + (keyIncludesTarget ? $" -> {Target(a)}" : "");

                DiffElements(interfaceChanges, CkModelElementKind.InterfaceAssociation, baselineInterface.Associations,
                    currentInterface.Associations, AssociationKey,
                    (memberChanges, memberId, baselineMember, currentMember) =>
                    {
                        AddModified(memberChanges, CkModelElementKind.InterfaceAssociation, memberId, "target",
                            Target(baselineMember), Target(currentMember));
                        AddModified(memberChanges, CkModelElementKind.InterfaceAssociation, memberId, "multiplicity",
                            baselineMember.Multiplicity?.ToString(), currentMember.Multiplicity?.ToString());
                        AddModified(memberChanges, CkModelElementKind.InterfaceAssociation, memberId, "isOptional",
                            baselineMember.IsOptional, currentMember.IsOptional);
                    },
                    added => $"{Target(added)}, {(added.IsOptional ? "optional" : "required")}",
                    removed => $"{Target(removed)}, {(removed.IsOptional ? "optional" : "required")}");
                DiffMethods(interfaceChanges, CkModelElementKind.InterfaceMethod, id, baselineInterface.Methods,
                    currentInterface.Methods, modelName);
            });
    }

    private static bool HasDuplicateRole(List<CkInterfaceAssociationDto>? associations) =>
        (associations ?? []).GroupBy(a => a.CkRoleId.FullName).Any(g => g.Count() > 1);

    /// <summary>
    ///     CK v2 (AB#5667): <c>implements</c> entries of a type, compared as a set of references.
    /// </summary>
    private static void DiffTypeInterfaces(List<CkModelChange> changes, string typeId,
        List<CkId<CkInterfaceId>>? baseline, List<CkId<CkInterfaceId>>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.TypeInterface,
            baseline?.Select(i => FormatReference(i, modelName)!).Distinct().ToList(),
            current?.Select(i => FormatReference(i, modelName)!).Distinct().ToList(),
            reference => $"{typeId}/{reference}", (_, _, _, _) => { });
    }

    /// <summary>
    ///     CK v2 (AB#5669): methods of a type keyed by method id. The description is compared on its own (patch);
    ///     every other field is compared through one canonical rendering of the signature.
    /// </summary>
    private static void DiffTypeMethods(List<CkModelChange> changes, string typeId, List<CkMethodDto>? baseline,
        List<CkMethodDto>? current, string modelName) =>
        DiffMethods(changes, CkModelElementKind.TypeMethod, typeId, baseline, current, modelName);

    /// <summary>
    ///     Methods of a type or (F1.1-S5) an interface, keyed by method id. AB#6268: one change per method field
    ///     (kind, result, execution, authorization), parameters and error codes as members of their own
    ///     (<see cref="CkModelElementKind.MethodParameter" />, <see cref="CkModelElementKind.MethodError" />); the
    ///     rendered <c>signature</c> is still emitted as a readable before/after summary and classified
    ///     <see cref="CkSemVerLevel.None" /> — the level comes from the field changes.
    /// </summary>
    private static void DiffMethods(List<CkModelChange> changes, CkModelElementKind kind, string ownerId,
        List<CkMethodDto>? baseline, List<CkMethodDto>? current, string modelName)
    {
        DiffElements(changes, kind, baseline, current, m => $"{ownerId}/{m.MethodId}",
            (methodChanges, id, baselineMethod, currentMethod) =>
            {
                AddModified(methodChanges, kind, id, "description",
                    baselineMethod.Description, currentMethod.Description);
                AddModified(methodChanges, kind, id, "signature",
                    FormatMethod(baselineMethod, modelName), FormatMethod(currentMethod, modelName));
                AddModified(methodChanges, kind, id, "kind", baselineMethod.Kind.ToString(), currentMethod.Kind.ToString());
                AddModified(methodChanges, kind, id, "result",
                    FormatMethodResult(baselineMethod, modelName), FormatMethodResult(currentMethod, modelName));
                AddModified(methodChanges, kind, id, "timeoutSeconds",
                    FormatTimeout(baselineMethod), FormatTimeout(currentMethod));
                AddModified(methodChanges, kind, id, "idempotent",
                    baselineMethod.Execution?.Idempotent ?? false, currentMethod.Execution?.Idempotent ?? false);
                // AB#6338: a block added or removed is ONE change carrying both blocks (FormatAuthorization), so the
                // classifier can compare them under default-deny; the field lines are only emitted when both versions
                // declare a block (no contradictory "removed — looser" + "narrowed" lines).
                if ((baselineMethod.Authorization == null) != (currentMethod.Authorization == null))
                {
                    AddModified(methodChanges, kind, id, "authorization",
                        FormatAuthorization(baselineMethod.Authorization), FormatAuthorization(currentMethod.Authorization));
                }
                else
                {
                    AddModified(methodChanges, kind, id, "roles",
                        FormatNameSet(baselineMethod.Authorization?.Roles), FormatNameSet(currentMethod.Authorization?.Roles));
                    AddModified(methodChanges, kind, id, "scopes",
                        FormatNameSet(baselineMethod.Authorization?.Scopes), FormatNameSet(currentMethod.Authorization?.Scopes));
                    AddModified(methodChanges, kind, id, "allowSelf",
                        baselineMethod.Authorization?.AllowSelf ?? false, currentMethod.Authorization?.AllowSelf ?? false);
                }

                DiffElements(methodChanges, CkModelElementKind.MethodParameter, baselineMethod.Parameters,
                    currentMethod.Parameters, p => $"{id}/{p.Name}",
                    (parameterChanges, parameterId, baselineParameter, currentParameter) =>
                    {
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "valueType",
                            baselineParameter.ValueType.ToString(), currentParameter.ValueType.ToString());
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "valueCkRecordId",
                            FormatReference(baselineParameter.ValueCkRecordId, modelName),
                            FormatReference(currentParameter.ValueCkRecordId, modelName));
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "valueCkEnumId",
                            FormatReference(baselineParameter.ValueCkEnumId, modelName),
                            FormatReference(currentParameter.ValueCkEnumId, modelName));
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "isOptional",
                            baselineParameter.IsOptional, currentParameter.IsOptional);
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "sensitive",
                            baselineParameter.Sensitive, currentParameter.Sensitive);
                        AddModified(parameterChanges, CkModelElementKind.MethodParameter, parameterId, "description",
                            baselineParameter.Description, currentParameter.Description);
                    },
                    added => FormatParameter(added, modelName),
                    removed => FormatParameter(removed, modelName));

                DiffElements(methodChanges, CkModelElementKind.MethodError, baselineMethod.Errors,
                    currentMethod.Errors, e => $"{id}/{e.Code}",
                    (errorChanges, errorId, baselineError, currentError) =>
                        AddModified(errorChanges, CkModelElementKind.MethodError, errorId, "description",
                            baselineError.Description, currentError.Description));
            });
    }

    private static string ValueTypeText(AttributeValueTypesDto valueType, CkId<CkRecordId>? recordId,
        CkId<CkEnumId>? enumId, string modelName) =>
        valueType + (recordId != null ? $"<{FormatReference(recordId, modelName)}>" : "") +
        (enumId != null ? $"<{FormatReference(enumId, modelName)}>" : "");

    private static string FormatParameter(CkMethodParameterDto parameter, string modelName) =>
        $"{ValueTypeText(parameter.ValueType, parameter.ValueCkRecordId, parameter.ValueCkEnumId, modelName)}, " +
        (parameter.IsOptional ? "optional" : "required") + (parameter.Sensitive ? ", sensitive" : "");

    private static string FormatMethodResult(CkMethodDto method, string modelName) =>
        method.Result == null
            ? "none"
            : ValueTypeText(method.Result.ValueType, method.Result.ValueCkRecordId, method.Result.ValueCkEnumId, modelName);

    private static string FormatTimeout(CkMethodDto method) =>
        (method.Execution?.TimeoutSeconds ?? CkMethodExecutionDto.DefaultTimeoutSeconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     AB#6338: <c>none</c> for an omitted block, otherwise <c>roles [..]; allowSelf ..; scopes [..]</c> (sorted,
    ///     de-duplicated sets) — parsed back by the classifier.
    /// </summary>
    internal static string FormatAuthorization(CkMethodAuthorizationDto? authorization) =>
        authorization == null
            ? "none"
            : $"roles [{FormatNameSet(authorization.Roles)}]; allowSelf {FormatBool(authorization.AllowSelf)}; " +
              $"scopes [{FormatNameSet(authorization.Scopes)}]";

    private static string FormatNameSet(IEnumerable<string>? names) =>
        string.Join(", ", (names ?? []).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal));

    internal static string FormatMethod(CkMethodDto method, string modelName)
    {
        string Value(AttributeValueTypesDto valueType, CkId<CkRecordId>? recordId, CkId<CkEnumId>? enumId) =>
            valueType + (recordId != null ? $"<{FormatReference(recordId, modelName)}>" : "") +
            (enumId != null ? $"<{FormatReference(enumId, modelName)}>" : "");

        var parameters = string.Join(", ", (method.Parameters ?? []).Select(p =>
            $"{p.Name}{(p.IsOptional ? "?" : "")}: {Value(p.ValueType, p.ValueCkRecordId, p.ValueCkEnumId)}" +
            (p.Sensitive ? " sensitive" : "")));
        var result = method.Result == null
            ? "none"
            : Value(method.Result.ValueType, method.Result.ValueCkRecordId, method.Result.ValueCkEnumId);
        var errors = string.Join(", ", (method.Errors ?? []).Select(e => e.Code));
        var authorization = method.Authorization == null
            ? "none"
            : $"roles [{string.Join(", ", method.Authorization.Roles ?? [])}], allowSelf " +
              $"{FormatBool(method.Authorization.AllowSelf)}, scopes [{string.Join(", ", method.Authorization.Scopes ?? [])}]";
        var timeout = (method.Execution?.TimeoutSeconds ?? CkMethodExecutionDto.DefaultTimeoutSeconds)
            .ToString(CultureInfo.InvariantCulture);
        return $"{method.Kind} ({parameters}) -> {result}; errors [{errors}]; authorization {authorization}; " +
               $"timeout {timeout}s, idempotent {FormatBool(method.Execution?.Idempotent ?? false)}";
    }

    private static void DiffAttributes(List<CkModelChange> changes, List<CkAttributeDto>? baseline,
        List<CkAttributeDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.Attribute, baseline, current, a => a.AttributeId.FullName,
            (attributeChanges, id, baselineAttribute, currentAttribute) =>
            {
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "valueType",
                    baselineAttribute.ValueType.ToString(), currentAttribute.ValueType.ToString());
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "valueCkRecordId",
                    FormatReference(baselineAttribute.ValueCkRecordId, modelName),
                    FormatReference(currentAttribute.ValueCkRecordId, modelName));
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "valueCkEnumId",
                    FormatReference(baselineAttribute.ValueCkEnumId, modelName),
                    FormatReference(currentAttribute.ValueCkEnumId, modelName));
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "defaultValues",
                    FormatValueList(baselineAttribute.DefaultValues), FormatValueList(currentAttribute.DefaultValues));
                // AB#5187: both markers are compared on their RESOLVED value, so migrating a
                // declaration from `isRuntimeState: true` to `ownership: RuntimeState` is not a
                // change — same meaning, no version bump owed. `isRuntimeState` resolves through
                // the DTO's mirror (it returns the ownership-derived value once ownership is
                // declared), so the two comparisons agree by construction.
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "isRuntimeState",
                    baselineAttribute.IsRuntimeState, currentAttribute.IsRuntimeState);
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "ownership",
                    ResolveOwnership(baselineAttribute).ToString(), ResolveOwnership(currentAttribute).ToString());
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "metaData",
                    FormatMetaData(baselineAttribute.MetaData), FormatMetaData(currentAttribute.MetaData));
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "description",
                    baselineAttribute.Description, currentAttribute.Description);
                // AB#6269: compared on the effective value, an omitted flag and 'securitySensitive: false' are equal.
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "securitySensitive",
                    baselineAttribute.SecuritySensitive ?? false, currentAttribute.SecuritySensitive ?? false);
            });
    }

    private static void DiffEnums(List<CkModelChange> changes, List<CkEnumDto>? baseline, List<CkEnumDto>? current)
    {
        DiffElements(changes, CkModelElementKind.Enum, baseline, current, e => e.EnumId.FullName,
            (enumChanges, id, baselineEnum, currentEnum) =>
            {
                AddModified(enumChanges, CkModelElementKind.Enum, id, "useFlags", baselineEnum.UseFlags, currentEnum.UseFlags);
                AddModified(enumChanges, CkModelElementKind.Enum, id, "isExtensible",
                    baselineEnum.IsExtensible, currentEnum.IsExtensible);
                AddModified(enumChanges, CkModelElementKind.Enum, id, "description",
                    baselineEnum.Description, currentEnum.Description);

                DiffElements(enumChanges, CkModelElementKind.EnumValue,
                    baselineEnum.Values?.ToList(), currentEnum.Values?.ToList(), v => $"{id}/{v.Name}",
                    (valueChanges, valueId, baselineValue, currentValue) =>
                    {
                        AddModified(valueChanges, CkModelElementKind.EnumValue, valueId, "key",
                            baselineValue.Key.ToString(CultureInfo.InvariantCulture),
                            currentValue.Key.ToString(CultureInfo.InvariantCulture));
                        AddModified(valueChanges, CkModelElementKind.EnumValue, valueId, "isExtension",
                            baselineValue.IsExtension, currentValue.IsExtension);
                        AddModified(valueChanges, CkModelElementKind.EnumValue, valueId, "description",
                            baselineValue.Description, currentValue.Description);
                    });
            });
    }

    private static void DiffRecords(List<CkModelChange> changes, List<CkRecordDto>? baseline,
        List<CkRecordDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.Record, baseline, current, r => r.RecordId.FullName,
            (recordChanges, id, baselineRecord, currentRecord) =>
            {
                AddModified(recordChanges, CkModelElementKind.Record, id, "derivedFromCkRecordId",
                    FormatReference(baselineRecord.DerivedFromCkRecordId, modelName),
                    FormatReference(currentRecord.DerivedFromCkRecordId, modelName));
                AddModified(recordChanges, CkModelElementKind.Record, id, "isFinal", baselineRecord.IsFinal, currentRecord.IsFinal);
                AddModified(recordChanges, CkModelElementKind.Record, id, "isAbstract", baselineRecord.IsAbstract, currentRecord.IsAbstract);
                AddModified(recordChanges, CkModelElementKind.Record, id, "recordKey",
                    baselineRecord.RecordKey, currentRecord.RecordKey);
                AddModified(recordChanges, CkModelElementKind.Record, id, "description",
                    baselineRecord.Description, currentRecord.Description);

                DiffAttributeAssignments(recordChanges, CkModelElementKind.RecordAttribute, id,
                    baselineRecord.Attributes, currentRecord.Attributes, modelName);
            });
    }

    private static void DiffAssociationRoles(List<CkModelChange> changes, List<CkAssociationRoleDto>? baseline,
        List<CkAssociationRoleDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.AssociationRole, baseline, current, r => r.AssociationRoleId.FullName,
            (roleChanges, id, baselineRole, currentRole) =>
            {
                AddModified(roleChanges, CkModelElementKind.AssociationRole, id, "inboundName",
                    baselineRole.InboundName, currentRole.InboundName);
                AddModified(roleChanges, CkModelElementKind.AssociationRole, id, "outboundName",
                    baselineRole.OutboundName, currentRole.OutboundName);
                AddModified(roleChanges, CkModelElementKind.AssociationRole, id, "inboundMultiplicity",
                    baselineRole.InboundMultiplicity.ToString(), currentRole.InboundMultiplicity.ToString());
                AddModified(roleChanges, CkModelElementKind.AssociationRole, id, "outboundMultiplicity",
                    baselineRole.OutboundMultiplicity.ToString(), currentRole.OutboundMultiplicity.ToString());
                AddModified(roleChanges, CkModelElementKind.AssociationRole, id, "description",
                    baselineRole.Description, currentRole.Description);

                DiffAttributeAssignments(roleChanges, CkModelElementKind.AssociationRoleAttribute, id,
                    baselineRole.Attributes, currentRole.Attributes, modelName);
            });
    }

    private static void DiffAttributeAssignments(List<CkModelChange> changes, CkModelElementKind elementKind,
        string ownerId, List<CkTypeAttributeDto>? baseline, List<CkTypeAttributeDto>? current, string modelName)
    {
        DiffElements(changes, elementKind, baseline, current, a => $"{ownerId}/{a.AttributeName}",
            (assignmentChanges, id, baselineAssignment, currentAssignment) =>
            {
                AddModified(assignmentChanges, elementKind, id, "id",
                    FormatReference(baselineAssignment.CkAttributeId, modelName),
                    FormatReference(currentAssignment.CkAttributeId, modelName));
                AddModified(assignmentChanges, elementKind, id, "isOptional",
                    baselineAssignment.IsOptional, currentAssignment.IsOptional);
                AddModified(assignmentChanges, elementKind, id, "autoCompleteValues",
                    FormatValueList(baselineAssignment.AutoCompleteValues), FormatValueList(currentAssignment.AutoCompleteValues));
                AddModified(assignmentChanges, elementKind, id, "autoIncrementReference",
                    baselineAssignment.AutoIncrementReference, currentAssignment.AutoIncrementReference);
                // AB#5187: the per-assignment ownership override is compared RAW (null = inherit
                // from the attribute definition). Adding or removing an override changes who wins
                // on this assignment only, which is why it is diffed here and not folded into the
                // definition's ownership change.
                AddModified(assignmentChanges, elementKind, id, "ownership",
                    baselineAssignment.Ownership?.ToString(), currentAssignment.Ownership?.ToString());
                // CK v2 (AB#5668): compared on the effective value — an omitted access and 'access: ReadWrite' are
                // the same declaration.
                AddModified(assignmentChanges, elementKind, id, "access",
                    AttributeAccess.Resolve(baselineAssignment.Access).ToString(),
                    AttributeAccess.Resolve(currentAssignment.Access).ToString());
            },
            added => FormatReference(added.CkAttributeId, modelName),
            removed => FormatReference(removed.CkAttributeId, modelName));
    }

    private static void DiffTypeAssociations(List<CkModelChange> changes, string typeId,
        List<CkTypeAssociationDto>? baseline, List<CkTypeAssociationDto>? current, string modelName)
    {
        string Key(CkTypeAssociationDto association) =>
            $"{FormatReference(association.CkRoleId, modelName)} -> {FormatReference(association.TargetCkTypeId, modelName)}";

        DiffElements(changes, CkModelElementKind.TypeAssociation, baseline, current,
            a => $"{typeId}/{Key(a)}",
            (associationChanges, id, baselineAssociation, currentAssociation) =>
            {
                AddModified(associationChanges, CkModelElementKind.TypeAssociation, id, "targetCkAttributeIds",
                    FormatReferenceList(baselineAssociation.TargetCkAttributeIds, modelName),
                    FormatReferenceList(currentAssociation.TargetCkAttributeIds, modelName));
                // F1.1-S5
                AddModified(associationChanges, CkModelElementKind.TypeAssociation, id, "targetCkInterfaceId",
                    FormatReference(baselineAssociation.TargetCkInterfaceId, modelName),
                    FormatReference(currentAssociation.TargetCkInterfaceId, modelName));
            });
    }

    private static void DiffTypeIndexes(List<CkModelChange> changes, string typeId,
        List<CkTypeIndexDto>? baseline, List<CkTypeIndexDto>? current)
    {
        var baselineSet = new HashSet<string>((baseline ?? []).Select(FormatIndex));
        var currentSet = new HashSet<string>((current ?? []).Select(FormatIndex));

        foreach (var index in currentSet.Where(i => !baselineSet.Contains(i)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Added, ElementKind = CkModelElementKind.TypeIndex,
                ElementId = $"{typeId}/index", NewValue = index
            });
        }

        foreach (var index in baselineSet.Where(i => !currentSet.Contains(i)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Removed, ElementKind = CkModelElementKind.TypeIndex,
                ElementId = $"{typeId}/index", OldValue = index
            });
        }
    }

    /// <summary>
    ///     Generic add/remove/modify walk over two element lists joined by a key selector.
    ///     Elements only present on one side produce Added/Removed changes; elements present on
    ///     both sides are descended into via <paramref name="diffMatched" />.
    /// </summary>
    private static void DiffElements<T>(List<CkModelChange> changes, CkModelElementKind elementKind,
        IReadOnlyCollection<T>? baseline, IReadOnlyCollection<T>? current, Func<T, string> keySelector,
        Action<List<CkModelChange>, string, T, T> diffMatched,
        Func<T, string?>? addedValueSelector = null, Func<T, string?>? removedValueSelector = null)
    {
        var baselineById = (baseline ?? Array.Empty<T>()).ToDictionary(keySelector);
        var currentById = (current ?? Array.Empty<T>()).ToDictionary(keySelector);

        foreach (var pair in currentById.Where(pair => !baselineById.ContainsKey(pair.Key)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Added, ElementKind = elementKind, ElementId = pair.Key,
                NewValue = addedValueSelector?.Invoke(pair.Value)
            });
        }

        foreach (var pair in baselineById.Where(pair => !currentById.ContainsKey(pair.Key)))
        {
            changes.Add(new CkModelChange
            {
                ChangeKind = CkModelChangeKind.Removed, ElementKind = elementKind, ElementId = pair.Key,
                OldValue = removedValueSelector?.Invoke(pair.Value)
            });
        }

        foreach (var pair in currentById)
        {
            if (baselineById.TryGetValue(pair.Key, out var baselineElement))
            {
                diffMatched(changes, pair.Key, baselineElement, pair.Value);
            }
        }
    }

    private static void AddModified(List<CkModelChange> changes, CkModelElementKind elementKind, string elementId,
        string property, string? oldValue, string? newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add(new CkModelChange
        {
            ChangeKind = CkModelChangeKind.Modified, ElementKind = elementKind, ElementId = elementId,
            Property = property, OldValue = oldValue, NewValue = newValue
        });
    }

    private static void AddModified(List<CkModelChange> changes, CkModelElementKind elementKind, string elementId,
        string property, bool oldValue, bool newValue)
    {
        AddModified(changes, elementKind, elementId, property, FormatBool(oldValue), FormatBool(newValue));
    }

    /// <summary>
    ///     Effective ownership of an attribute DEFINITION for diff purposes (AB#5187): the
    ///     declared <c>ownership</c>, or the deprecated <c>isRuntimeState</c> alias mapped onto it.
    ///     Comparing the resolved value means a baseline compiled before ownership existed and a
    ///     current model that only renamed its marker produce no change — the migration from the
    ///     boolean to the enum costs no version bump, only a genuine semantic change does.
    /// </summary>
    private static AttributeOwnershipDto ResolveOwnership(CkAttributeDto attribute)
    {
        return AttributeOwnership.Resolve(attribute.Ownership, attribute.IsRuntimeState);
    }

    /// <summary>
    ///     Renders a reference for comparison and display. Self references ignore the model
    ///     version, foreign references keep the semantic (major) version — see the class remarks.
    /// </summary>
    private static string? FormatReference<TElementId>(CkId<TElementId>? reference, string modelName)
        where TElementId : IComparable<TElementId>, ICkElementId
    {
        if (reference == null)
        {
            return null;
        }

        return reference.ModelId.Name == modelName
            ? $"{reference.ModelId.Name}/{reference.ElementId.FullName}"
            : $"{reference.ModelId.SemanticVersionedFullName}/{reference.ElementId.FullName}";
    }

    private static string? FormatReferenceList<TElementId>(IReadOnlyCollection<CkId<TElementId>>? references, string modelName)
        where TElementId : IComparable<TElementId>, ICkElementId
    {
        if (references == null || references.Count == 0)
        {
            return null;
        }

        return string.Join(", ", references.Select(r => FormatReference(r, modelName)).OrderBy(r => r, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Canonical scalar rendering so that baseline models (deserialized from the catalog JSON)
    ///     and current models (compiled from YAML) compare equal when semantically identical,
    ///     regardless of the boxed CLR type (e.g. <c>short 5</c> vs. <c>int 5</c>).
    /// </summary>
    private static string? FormatValueList(IEnumerable<object>? values)
    {
        if (values == null)
        {
            return null;
        }

        var rendered = values.Select(FormatScalar).ToList();
        return rendered.Count == 0 ? null : $"[{string.Join(", ", rendered)}]";
    }

    private static string FormatScalar(object? value)
    {
        return value switch
        {
            null => "null",
            bool b => FormatBool(b),
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null"
        };
    }

    private static string FormatBool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string? FormatMetaData(IEnumerable<CkAttributeMetaDataDto>? metaData)
    {
        if (metaData == null)
        {
            return null;
        }

        var rendered = metaData
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => m.Description == null ? $"{m.Key}={m.Value}" : $"{m.Key}={m.Value} ({m.Description})")
            .ToList();
        return rendered.Count == 0 ? null : string.Join("; ", rendered);
    }

    private static string FormatIndex(CkTypeIndexDto index)
    {
        var fields = index.Fields
            .Select(f => f.Weight == null
                ? string.Join("+", f.AttributePaths)
                : $"{string.Join("+", f.AttributePaths)}(weight {f.Weight.Value.ToString(CultureInfo.InvariantCulture)})")
            .ToList();
        var language = string.IsNullOrEmpty(index.Language) ? "" : $", language {index.Language}";
        return $"{index.IndexType} on {string.Join("; ", fields)}{language}";
    }
}
