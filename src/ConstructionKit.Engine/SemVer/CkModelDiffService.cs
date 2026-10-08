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
    ///     Registry of all DTO properties this diff accounts for (either compared or knowingly
    ///     excluded from comparison). The classification guard test asserts that every public
    ///     property of the element DTOs appears here, so a new schema field cannot be added
    ///     without a conscious diff and classification decision.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, IReadOnlyCollection<string>> AccountedProperties =
        new Dictionary<Type, IReadOnlyCollection<string>>
        {
            // SchemaUri is a serialization constant; Migrations are not part of the schema
            // contract (they always accompany a version bump by design) and are reconciled
            // separately by the migration check of the ValidateVersion command.
            // DependencyRanges / IsRangeRetaining (CK v2 range retention, AB#5664): conscious exclusion in the
            // Phase 0 spike — the classification of range/floor changes (only a raised floor or a changed range
            // counts) is Phase 2 (F2.1). Dependencies keeps carrying the exact closure and is still diffed.
            [typeof(CkCompiledModelRoot)] = [nameof(CkCompiledModelRoot.SchemaUri), nameof(CkCompiledModelRoot.Dependencies), nameof(CkCompiledModelRoot.Migrations),
                nameof(CkCompiledModelRoot.DependencyRanges), nameof(CkCompiledModelRoot.IsRangeRetaining)],
            [typeof(CkModelRootBase)] =
            [
                nameof(CkModelRootBase.Types), nameof(CkModelRootBase.AssociationRoles), nameof(CkModelRootBase.Attributes),
                nameof(CkModelRootBase.Records), nameof(CkModelRootBase.Enums), nameof(CkModelRootBase.Interfaces)
            ],
            // EffectiveCkLanguage is a computed view of CkLanguage (not serialized).
            [typeof(CkModelPropertiesDto)] = [nameof(CkModelPropertiesDto.ModelId), nameof(CkModelPropertiesDto.Description),
                nameof(CkModelPropertiesDto.CkLanguage), nameof(CkModelPropertiesDto.EffectiveCkLanguage)],
            [typeof(CkCompiledTypeDto)] = [nameof(CkCompiledTypeDto.IsCollectionRoot)],
            [typeof(CkTypeDto)] =
            [
                nameof(CkTypeDto.TypeId), nameof(CkTypeDto.DerivedFromCkTypeId), nameof(CkTypeDto.IsFinal),
                nameof(CkTypeDto.IsAbstract), nameof(CkTypeDto.Indexes), nameof(CkTypeDto.Associations),
                nameof(CkTypeDto.EnableChangeStreamPreAndPostImages), nameof(CkTypeDto.Description),
                nameof(CkTypeDto.DisplayNameRule), nameof(CkTypeDto.DisplayDescriptionRule),
                nameof(CkTypeDto.OwnerAttributePath), nameof(CkTypeDto.Implements), nameof(CkTypeDto.Methods)
            ],
            [typeof(CkTypeWithAttributesDto)] = [nameof(CkTypeWithAttributesDto.Attributes)],
            [typeof(CkAttributeDto)] =
            [
                nameof(CkAttributeDto.AttributeId), nameof(CkAttributeDto.ValueType), nameof(CkAttributeDto.ValueCkRecordId),
                nameof(CkAttributeDto.ValueCkEnumId), nameof(CkAttributeDto.DefaultValues), nameof(CkAttributeDto.IsRuntimeState),
                nameof(CkAttributeDto.Ownership),
                nameof(CkAttributeDto.Description), nameof(CkAttributeDto.MetaData)
            ],
            [typeof(CkEnumDto)] =
            [
                nameof(CkEnumDto.EnumId), nameof(CkEnumDto.UseFlags), nameof(CkEnumDto.IsExtensible),
                nameof(CkEnumDto.Values), nameof(CkEnumDto.Description)
            ],
            [typeof(CkEnumValueDto)] =
            [
                nameof(CkEnumValueDto.Key), nameof(CkEnumValueDto.Name), nameof(CkEnumValueDto.Description),
                nameof(CkEnumValueDto.IsExtension)
            ],
            [typeof(CkRecordDto)] =
            [
                nameof(CkRecordDto.RecordId), nameof(CkRecordDto.DerivedFromCkRecordId), nameof(CkRecordDto.IsFinal),
                nameof(CkRecordDto.IsAbstract), nameof(CkRecordDto.Description), nameof(CkRecordDto.RecordKey)
            ],
            [typeof(CkAssociationRoleDto)] =
            [
                nameof(CkAssociationRoleDto.AssociationRoleId), nameof(CkAssociationRoleDto.InboundName),
                nameof(CkAssociationRoleDto.OutboundName), nameof(CkAssociationRoleDto.InboundMultiplicity),
                nameof(CkAssociationRoleDto.OutboundMultiplicity), nameof(CkAssociationRoleDto.Description)
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
                nameof(CkTypeAssociationDto.TargetCkAttributeIds)
            ],
            [typeof(CkTypeIndexDto)] = [nameof(CkTypeIndexDto.IndexType), nameof(CkTypeIndexDto.Language), nameof(CkTypeIndexDto.Fields)],
            [typeof(CkIndexFieldsDto)] = [nameof(CkIndexFieldsDto.Weight), nameof(CkIndexFieldsDto.AttributePaths)],
            [typeof(CkAttributeMetaDataDto)] = [nameof(CkAttributeMetaDataDto.Key), nameof(CkAttributeMetaDataDto.Value), nameof(CkAttributeMetaDataDto.Description)],
            // CK v2 (AB#5667 / AB#5669)
            [typeof(CkInterfaceDto)] = [nameof(CkInterfaceDto.InterfaceId), nameof(CkInterfaceDto.Description), nameof(CkInterfaceDto.Attributes)],
            [typeof(CkInterfaceAttributeDto)] =
            [
                nameof(CkInterfaceAttributeDto.CkAttributeId), nameof(CkInterfaceAttributeDto.AttributeName),
                nameof(CkInterfaceAttributeDto.IsOptional)
            ],
            [typeof(CkMethodDto)] =
            [
                nameof(CkMethodDto.MethodId), nameof(CkMethodDto.Kind), nameof(CkMethodDto.Description),
                nameof(CkMethodDto.Parameters), nameof(CkMethodDto.Result), nameof(CkMethodDto.Errors),
                nameof(CkMethodDto.Authorization), nameof(CkMethodDto.Execution)
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
        DiffTypes(changes, baseline.Types, current.Types, modelName);
        DiffAttributes(changes, baseline.Attributes, current.Attributes, modelName);
        DiffEnums(changes, baseline.Enums, current.Enums);
        DiffRecords(changes, baseline.Records, current.Records, modelName);
        DiffAssociationRoles(changes, baseline.AssociationRoles, current.AssociationRoles, modelName);
        DiffInterfaces(changes, baseline.Interfaces, current.Interfaces, modelName);

        return changes;
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
                    added => FormatReference(added.CkAttributeId, modelName),
                    removed => FormatReference(removed.CkAttributeId, modelName));
            });
    }

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
        List<CkMethodDto>? current, string modelName)
    {
        DiffElements(changes, CkModelElementKind.TypeMethod, baseline, current, m => $"{typeId}/{m.MethodId}",
            (methodChanges, id, baselineMethod, currentMethod) =>
            {
                AddModified(methodChanges, CkModelElementKind.TypeMethod, id, "description",
                    baselineMethod.Description, currentMethod.Description);
                AddModified(methodChanges, CkModelElementKind.TypeMethod, id, "signature",
                    FormatMethod(baselineMethod, modelName), FormatMethod(currentMethod, modelName));
                // Review L16: parameter and error descriptions are documentation, not part of the signature.
                AddModified(methodChanges, CkModelElementKind.TypeMethod, id, "documentation",
                    FormatMethodDocumentation(baselineMethod), FormatMethodDocumentation(currentMethod));
            });
    }

    /// <summary>Parameter and error descriptions of a method (documentation only, review L16).</summary>
    private static string FormatMethodDocumentation(CkMethodDto method)
    {
        var parameters = string.Join(", ", (method.Parameters ?? [])
            .Where(p => p.Description != null).Select(p => $"{p.Name} \"{p.Description}\""));
        var errors = string.Join(", ", (method.Errors ?? [])
            .Where(e => e.Description != null).Select(e => $"{e.Code} \"{e.Description}\""));
        return $"parameters [{parameters}]; errors [{errors}]";
    }

    private static string FormatMethod(CkMethodDto method, string modelName)
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
