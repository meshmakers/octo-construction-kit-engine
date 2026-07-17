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
            [typeof(CkCompiledModelRoot)] = [nameof(CkCompiledModelRoot.SchemaUri), nameof(CkCompiledModelRoot.Dependencies), nameof(CkCompiledModelRoot.Migrations)],
            [typeof(CkModelRootBase)] =
            [
                nameof(CkModelRootBase.Types), nameof(CkModelRootBase.AssociationRoles), nameof(CkModelRootBase.Attributes),
                nameof(CkModelRootBase.Records), nameof(CkModelRootBase.Enums)
            ],
            [typeof(CkModelPropertiesDto)] = [nameof(CkModelPropertiesDto.ModelId), nameof(CkModelPropertiesDto.Description)],
            [typeof(CkCompiledTypeDto)] = [nameof(CkCompiledTypeDto.IsCollectionRoot)],
            [typeof(CkTypeDto)] =
            [
                nameof(CkTypeDto.TypeId), nameof(CkTypeDto.DerivedFromCkTypeId), nameof(CkTypeDto.IsFinal),
                nameof(CkTypeDto.IsAbstract), nameof(CkTypeDto.Indexes), nameof(CkTypeDto.Associations),
                nameof(CkTypeDto.EnableChangeStreamPreAndPostImages), nameof(CkTypeDto.Description)
            ],
            [typeof(CkTypeWithAttributesDto)] = [nameof(CkTypeWithAttributesDto.Attributes)],
            [typeof(CkAttributeDto)] =
            [
                nameof(CkAttributeDto.AttributeId), nameof(CkAttributeDto.ValueType), nameof(CkAttributeDto.ValueCkRecordId),
                nameof(CkAttributeDto.ValueCkEnumId), nameof(CkAttributeDto.DefaultValues), nameof(CkAttributeDto.IsRuntimeState),
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
                nameof(CkRecordDto.IsAbstract), nameof(CkRecordDto.Description)
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
                nameof(CkTypeAttributeDto.IsOptional)
            ],
            [typeof(CkTypeAssociationDto)] =
            [
                nameof(CkTypeAssociationDto.CkRoleId), nameof(CkTypeAssociationDto.TargetCkTypeId),
                nameof(CkTypeAssociationDto.TargetCkAttributeIds)
            ],
            [typeof(CkTypeIndexDto)] = [nameof(CkTypeIndexDto.IndexType), nameof(CkTypeIndexDto.Language), nameof(CkTypeIndexDto.Fields)],
            [typeof(CkIndexFieldsDto)] = [nameof(CkIndexFieldsDto.Weight), nameof(CkIndexFieldsDto.AttributePaths)],
            [typeof(CkAttributeMetaDataDto)] = [nameof(CkAttributeMetaDataDto.Key), nameof(CkAttributeMetaDataDto.Value), nameof(CkAttributeMetaDataDto.Description)]
        };

    /// <inheritdoc />
    public IReadOnlyList<CkModelChange> Diff(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = new List<CkModelChange>();
        var modelName = current.ModelId.Name;

        AddModified(changes, CkModelElementKind.Model, modelName, "description", baseline.Description, current.Description);

        DiffDependencies(changes, baseline.Dependencies, current.Dependencies);
        DiffTypes(changes, baseline.Types, current.Types, modelName);
        DiffAttributes(changes, baseline.Attributes, current.Attributes, modelName);
        DiffEnums(changes, baseline.Enums, current.Enums);
        DiffRecords(changes, baseline.Records, current.Records, modelName);
        DiffAssociationRoles(changes, baseline.AssociationRoles, current.AssociationRoles, modelName);

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

                DiffAttributeAssignments(typeChanges, CkModelElementKind.TypeAttribute, id,
                    baselineType.Attributes, currentType.Attributes, modelName);
                DiffTypeAssociations(typeChanges, id, baselineType.Associations, currentType.Associations, modelName);
                DiffTypeIndexes(typeChanges, id, baselineType.Indexes, currentType.Indexes);
            });
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
                AddModified(attributeChanges, CkModelElementKind.Attribute, id, "isRuntimeState",
                    baselineAttribute.IsRuntimeState, currentAttribute.IsRuntimeState);
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
