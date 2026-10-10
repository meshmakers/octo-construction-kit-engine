using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Versioning;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Implementation of <see cref="IElementResolver" /> that resolves the elements of a compiled model.
/// </summary>
internal class ElementResolver : IElementResolver
{
    /// <inheritdoc />
    public void Resolve(CkModelRootBase modelRootBase, CkModelGraph ckModelGraph, IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        ckModelGraph.GetOrCreateModel(modelRootBase.ModelId, modelRootBase.Description).CkLanguage =
            modelRootBase.CkLanguage;

        // CK v2 (AB#5584): language version gate (91) and feature gate (90).
        CheckCkLanguage(modelRootBase, originFileResolver, operationResult);

        // F1.2-S2 (review N5): Hidden assignments that leak through other constructs.
        CheckHiddenAssignments(modelRootBase, originFileResolver, operationResult);

        // F1.4-S2 (review L15): generated method names must be unique per model.
        CheckGeneratedMethodNames(modelRootBase, originFileResolver, operationResult);

        if (modelRootBase.Interfaces != null)
        {
            ResolveInterfaces(modelRootBase, ckModelGraph, variableResolver, originFileResolver, operationResult);
        }

        if (modelRootBase.Attributes != null)
        {
            foreach (var ckAttribute in modelRootBase.Attributes)
            {
                var ckAttributeId = new CkId<CkAttributeId>(modelRootBase.ModelId, ckAttribute.AttributeId);

                if (!Regex.IsMatch(ckAttribute.AttributeId.Name, CompilerStatics.PascalCaseRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdContainsInvalidCharacters(
                        originFileResolver.Resolve(ckAttributeId),
                        ckAttribute.AttributeId.Name));
                    continue;
                }

                if (ckModelGraph.Attributes.ContainsKey(ckAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.AttributeIdNotUnique(originFileResolver.Resolve(ckAttributeId), ckAttributeId));
                    continue;
                }

                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record ||
                     ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId == null)
                {
                    operationResult.AddMessage(
                        MessageCodes.CkRecordIdUndefined(originFileResolver.Resolve(ckAttributeId), ckAttributeId));
                    continue;
                }

                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record ||
                     ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId != null)
                {
                    ckAttribute.ValueCkRecordId = variableResolver.Resolve(ckAttribute.ValueCkRecordId.FullName,
                        originFileResolver.Resolve(ckAttributeId), operationResult);
                }

                if (ckAttribute is { ValueType: AttributeValueTypesDto.Enum, ValueCkEnumId: null })
                {
                    operationResult.AddMessage(MessageCodes.CkEnumIdUndefined(originFileResolver.Resolve(ckAttributeId),
                        ckAttributeId));
                    continue;
                }

                if (ckAttribute is { ValueType: AttributeValueTypesDto.Enum, ValueCkEnumId: not null })
                {
                    ckAttribute.ValueCkEnumId =
                        variableResolver.Resolve(ckAttribute.ValueCkEnumId.FullName,
                            originFileResolver.Resolve(ckAttributeId), operationResult);
                }

                ckModelGraph.GetOrCreateAttribute(ckAttributeId, ckAttribute);
            }
        }

        if (modelRootBase.AssociationRoles != null)
        {
            foreach (var ckAssociationRole in modelRootBase.AssociationRoles)
            {
                var ckAssociationId =
                    new CkId<CkAssociationRoleId>(modelRootBase.ModelId, ckAssociationRole.AssociationRoleId);
                if (!Regex.IsMatch(ckAssociationRole.AssociationRoleId.RoleId,
                        CompilerStatics.PascalCaseRegex))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkAssociationIdContainsInvalidCharacters(
                            originFileResolver.Resolve(ckAssociationId),
                            ckAssociationRole.AssociationRoleId.RoleId));
                    continue;
                }

                if (ckModelGraph.AssociationRoles.ContainsKey(ckAssociationId))
                {
                    operationResult.AddMessage(
                        MessageCodes.AssociationRoleIdNotUnique(originFileResolver.Resolve(ckAssociationId),
                            ckAssociationId));
                    continue;
                }

                if (ckAssociationRole.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute ids
                    var duplicateAttributeIds = ckAssociationRole.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkAssociationRoleAttributeIdNotUnique(
                                originFileResolver.Resolve(ckAssociationId), ckAssociationRole.AssociationRoleId,
                                string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckAssociationRole.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkAssociationRoleAttributeNameNotUnique(
                                originFileResolver.Resolve(ckAssociationId), ckAssociationRole.AssociationRoleId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }

                    foreach (var ckTypeAttributeDto in ckAssociationRole.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId = variableResolver.Resolve(
                            ckTypeAttributeDto.CkAttributeId.FullName, originFileResolver.Resolve(ckAssociationId),
                            operationResult);
                    }
                }

                ckModelGraph.GetOrCreateAssociationRole(ckAssociationId, ckAssociationRole);
            }
        }

        if (modelRootBase.Types != null)
        {
            foreach (var ckType in modelRootBase.Types)
            {
                var ckTypeId = new CkId<CkTypeId>(modelRootBase.ModelId, ckType.TypeId);
                if (!Regex.IsMatch(ckType.TypeId.Name, CompilerStatics.PascalCaseRegex))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkTypeIdContainsInvalidCharacters(originFileResolver.Resolve(ckTypeId),
                            ckType.TypeId.Name));
                    continue;
                }

                if (ckModelGraph.Types.ContainsKey(ckTypeId))
                {
                    operationResult.AddMessage(MessageCodes.TypeIdNotUnique(originFileResolver.Resolve(ckTypeId),
                        ckTypeId));
                    continue;
                }

                if (ckType.DerivedFromCkTypeId != null)
                {
                    ckType.DerivedFromCkTypeId =
                        variableResolver.Resolve(ckType.DerivedFromCkTypeId.FullName,
                            originFileResolver.Resolve(ckTypeId), operationResult);
                }

                if (ckType.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkType) have duplicate attribute ids
                    var duplicateAttributeIds = ckType.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeIdNotUnique(originFileResolver.Resolve(ckTypeId),
                                ckType.TypeId,
                                string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkType) have duplicate attribute names
                    var duplicateAttributeNames = ckType.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeNameNotUnique(originFileResolver.Resolve(ckTypeId),
                                ckType.TypeId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }

                    foreach (var ckTypeAttributeDto in ckType.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId =
                            variableResolver.Resolve(ckTypeAttributeDto.CkAttributeId.FullName,
                                originFileResolver.Resolve(ckTypeId), operationResult);
                    }
                }

                // CK v2: implements and method value references go through the variable resolver exactly like
                // derivedFromCkTypeId, so range retention (F0.2) applies to them without a separate path.
                if (ckType.Implements != null)
                {
                    ckType.Implements = ckType.Implements
                        .Select(i => (CkId<CkInterfaceId>)variableResolver.Resolve(i.FullName,
                            originFileResolver.Resolve(ckTypeId), operationResult))
                        .ToList();
                }

                ResolveMethodReferences(ckType.Methods, variableResolver, originFileResolver.Resolve(ckTypeId),
                    operationResult);

                if (ckType.Associations != null)
                {
                    foreach (var ckTypeAssociationDto in ckType.Associations)
                    {
                        ckTypeAssociationDto.CkRoleId =
                            variableResolver.Resolve(ckTypeAssociationDto.CkRoleId.FullName,
                                originFileResolver.Resolve(ckTypeId), operationResult);
                        ckTypeAssociationDto.TargetCkTypeId =
                            variableResolver.Resolve(ckTypeAssociationDto.TargetCkTypeId.FullName,
                                originFileResolver.Resolve(ckTypeId), operationResult);
                        ckTypeAssociationDto.TargetCkInterfaceId = ResolveReference(
                            ckTypeAssociationDto.TargetCkInterfaceId, variableResolver,
                            originFileResolver.Resolve(ckTypeId), operationResult);
                    }
                }

                ckModelGraph.GetOrCreateType(ckTypeId, ckType);
            }

            // CK v2 (AB#5667) rule I-5: interface and type names of a model share the GraphQL type namespace.
            foreach (var ckInterface in modelRootBase.Interfaces ?? [])
            {
                var collidingType = modelRootBase.Types.FirstOrDefault(t =>
                    t.TypeId.Name == ckInterface.InterfaceId.Name);
                if (collidingType != null)
                {
                    var ckInterfaceId = new CkId<CkInterfaceId>(modelRootBase.ModelId, ckInterface.InterfaceId);
                    operationResult.AddMessage(MessageCodes.CkInterfaceNameCollidesWithType(
                        originFileResolver.Resolve(ckInterfaceId), ckInterfaceId,
                        new CkId<CkTypeId>(modelRootBase.ModelId, collidingType.TypeId)));
                }
            }
        }

        if (modelRootBase.Records != null)
        {
            foreach (var ckRecord in modelRootBase.Records)
            {
                var ckRecordId = new CkId<CkRecordId>(modelRootBase.ModelId, ckRecord.RecordId);
                if (!Regex.IsMatch(ckRecord.RecordId.Name, CompilerStatics.PascalCaseRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkRecordIdContainsInvalidCharacters(
                        originFileResolver.Resolve(ckRecordId),
                        ckRecord.RecordId.Name));
                    continue;
                }

                if (ckModelGraph.Records.ContainsKey(ckRecordId))
                {
                    operationResult.AddMessage(MessageCodes.RecordIdNotUnique(originFileResolver.Resolve(ckRecordId),
                        ckRecordId));
                    continue;
                }

                if (ckRecord.DerivedFromCkRecordId != null)
                {
                    ckRecord.DerivedFromCkRecordId =
                        variableResolver.Resolve(ckRecord.DerivedFromCkRecordId.FullName,
                            originFileResolver.Resolve(ckRecordId), operationResult);
                }

                if (ckRecord.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute ids
                    var duplicateAttributeIds = ckRecord.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeIdNotUnique(originFileResolver.Resolve(ckRecordId),
                                ckRecord.RecordId, string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckRecord.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeNameNotUnique(originFileResolver.Resolve(ckRecordId),
                                ckRecord.RecordId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }

                    foreach (var ckTypeAttributeDto in ckRecord.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId =
                            variableResolver.Resolve(ckTypeAttributeDto.CkAttributeId.FullName,
                                originFileResolver.Resolve(ckRecordId), operationResult);
                    }
                }

                ckModelGraph.GetOrCreateRecord(ckRecordId, ckRecord);
            }
        }

        if (modelRootBase.Enums != null)
        {
            foreach (var ckEnum in modelRootBase.Enums)
            {
                var ckEnumId = new CkId<CkEnumId>(modelRootBase.ModelId, ckEnum.EnumId);
                if (!Regex.IsMatch(ckEnum.EnumId.Name, CompilerStatics.PascalCaseRegex))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkEnumIdContainsInvalidCharacters(originFileResolver.Resolve(ckEnumId),
                            ckEnum.EnumId.Name));
                    continue;
                }

                if (ckModelGraph.Enums.ContainsKey(ckEnumId))
                {
                    operationResult.AddMessage(MessageCodes.EnumIdNotUnique(originFileResolver.Resolve(ckEnumId),
                        ckEnumId));
                    continue;
                }

                var ignoreEnum = false;
                foreach (var ckEnumValueDto in ckEnum.Values)
                {
                    if (string.IsNullOrWhiteSpace(ckEnumValueDto.Name))
                    {
                        operationResult.AddMessage(
                            MessageCodes.EnumNameMyNotBeEmpty(originFileResolver.Resolve(ckEnumId), ckEnumId,
                                ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }

                    if (!Regex.IsMatch(ckEnumValueDto.Name, CompilerStatics.PascalCaseRegex))
                    {
                        operationResult.AddMessage(
                            MessageCodes.EnumNameMayNotContainWhitespaceSpecialCharacters(
                                originFileResolver.Resolve(ckEnumId), ckEnumId, ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }

                    if (ckEnumValueDto.Key < 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.EnumKeyMayNotBeNegative(originFileResolver.Resolve(ckEnumId), ckEnumId,
                                ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }
                }

                foreach (var ckSelectionValueGroup in
                         ckEnum.Values.GroupBy(x => x.Key).Where(x => x.Count() > 1))
                {
                    operationResult.AddMessage(MessageCodes.SelectionValueNotUnique(
                        originFileResolver.Resolve(ckEnumId), ckEnumId, ckSelectionValueGroup.Key));
                    ignoreEnum = true;
                }

                if (!ckEnum.IsExtensible && ckEnum.Values.Any(x => x.IsExtension))
                {
                    operationResult.AddMessage(
                        MessageCodes.EnumIsNotExtensibleButContainsExtension(originFileResolver.Resolve(ckEnumId),
                            ckEnumId));
                    ignoreEnum = true;
                }

                if (ignoreEnum)
                {
                    continue;
                }

                ckModelGraph.GetOrCreateEnum(ckEnumId, ckEnum);
            }
        }

        // F1.1-S4: effective visibility / derivable (derivable default depends on the CK language).
        ckModelGraph.ApplyCkV2Modifiers(modelRootBase);
    }

    /// <summary>
    ///     CK v2 (AB#5584): message 91 for a CK language version this engine does not support, message 90 for
    ///     every CK v2 feature used by a model that does not declare <c>ckLanguage: 2</c>.
    /// </summary>
    private static void CheckCkLanguage(CkModelRootBase model, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var location = originFileResolver.Resolve(model.ModelId);
        // F1.1-S6: a compiled model may require a newer engine (message 126).
        if (!CkEngineVersion.CheckModel(model, location, operationResult))
        {
            return;
        }

        if (model.CkLanguage is { } declared &&
            (declared < 1 || declared > CkModelPropertiesDto.MaxSupportedCkLanguage))
        {
            operationResult.AddMessage(MessageCodes.CkLanguageNotSupported(location, model.ModelId, declared,
                CkModelPropertiesDto.MaxSupportedCkLanguage));
            return;
        }

        if (model.EffectiveCkLanguage >= 2)
        {
            return;
        }

        var ckLanguage = model.EffectiveCkLanguage;

        void Report(object key, string feature, object element) =>
            operationResult.AddMessage(MessageCodes.CkLanguageFeatureRequiresV2(originFileResolver.Resolve(key),
                model.ModelId, feature, element, ckLanguage));

        void CheckAccess(object key, object owner, IEnumerable<CkTypeAttributeDto>? attributes)
        {
            foreach (var attribute in (attributes ?? []).Where(a => a.Access != null))
            {
                Report(key, "access", $"{owner}/{attribute.AttributeName}");
            }
        }

        foreach (var ckInterface in model.Interfaces ?? [])
        {
            var key = new CkId<CkInterfaceId>(model.ModelId, ckInterface.InterfaceId);
            Report(key, "interfaces", key);
        }

        // F1.1-S4: visibility / derivable are CK v2 keys.
        void CheckModifiers(object key, CkVisibilityDto? visibility, CkDerivableDto? derivable = null)
        {
            if (visibility != null)
            {
                Report(key, "visibility", key);
            }

            if (derivable != null)
            {
                Report(key, "derivable", key);
            }
        }

        foreach (var ckType in model.Types ?? [])
        {
            CheckModifiers(new CkId<CkTypeId>(model.ModelId, ckType.TypeId), ckType.Visibility, ckType.Derivable);
            // F1.1-S5: an interface as association target.
            foreach (var association in (ckType.Associations ?? []).Where(a => a.TargetCkInterfaceId != null))
            {
                Report(new CkId<CkTypeId>(model.ModelId, ckType.TypeId), "targetCkInterfaceId",
                    $"{new CkId<CkTypeId>(model.ModelId, ckType.TypeId)}/{association.CkRoleId}");
            }

            foreach (var method in (ckType.Methods ?? []).Where(m => m.Visibility != null))
            {
                Report(new CkId<CkTypeId>(model.ModelId, ckType.TypeId), "visibility",
                    $"{new CkId<CkTypeId>(model.ModelId, ckType.TypeId)}.{method.MethodId}");
            }
        }

        foreach (var ckRecord in model.Records ?? [])
        {
            CheckModifiers(new CkId<CkRecordId>(model.ModelId, ckRecord.RecordId), ckRecord.Visibility,
                ckRecord.Derivable);
        }

        foreach (var ckEnum in model.Enums ?? [])
        {
            CheckModifiers(new CkId<CkEnumId>(model.ModelId, ckEnum.EnumId), ckEnum.Visibility);
        }

        foreach (var ckAttribute in model.Attributes ?? [])
        {
            CheckModifiers(new CkId<CkAttributeId>(model.ModelId, ckAttribute.AttributeId), ckAttribute.Visibility);
            if (ckAttribute.SecuritySensitive != null)
            {
                // AB#6269
                Report(new CkId<CkAttributeId>(model.ModelId, ckAttribute.AttributeId), "securitySensitive",
                    new CkId<CkAttributeId>(model.ModelId, ckAttribute.AttributeId));
            }
        }

        foreach (var ckRole in model.AssociationRoles ?? [])
        {
            CheckModifiers(new CkId<CkAssociationRoleId>(model.ModelId, ckRole.AssociationRoleId), ckRole.Visibility);
        }

        foreach (var ckType in model.Types ?? [])
        {
            var key = new CkId<CkTypeId>(model.ModelId, ckType.TypeId);
            if (ckType.Implements is { Count: > 0 })
            {
                Report(key, "implements", key);
            }

            if (ckType.Methods is { Count: > 0 })
            {
                Report(key, "methods", key);
            }

            CheckAccess(key, key, ckType.Attributes);
        }

        foreach (var ckRecord in model.Records ?? [])
        {
            var key = new CkId<CkRecordId>(model.ModelId, ckRecord.RecordId);
            CheckAccess(key, key, ckRecord.Attributes);
        }

        foreach (var ckRole in model.AssociationRoles ?? [])
        {
            var key = new CkId<CkAssociationRoleId>(model.ModelId, ckRole.AssociationRoleId);
            CheckAccess(key, key, ckRole.Attributes);
        }
    }

    /// <summary>
    ///     CK v2 (AB#5667): interface id uniqueness (92; the PascalCase and version rules are enforced by the schema)
    ///     and variable resolution of the member attribute references.
    /// </summary>
    private static void ResolveInterfaces(CkModelRootBase model, CkModelGraph ckModelGraph,
        IVariableResolver variableResolver, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        foreach (var ckInterface in model.Interfaces!)
        {
            var ckInterfaceId = new CkId<CkInterfaceId>(model.ModelId, ckInterface.InterfaceId);
            var location = originFileResolver.Resolve(ckInterfaceId);
            if (ckModelGraph.Interfaces.ContainsKey(ckInterfaceId))
            {
                operationResult.AddMessage(MessageCodes.CkInterfaceIdNotUnique(location, ckInterfaceId));
                continue;
            }

            foreach (var member in ckInterface.Attributes)
            {
                member.CkAttributeId = variableResolver.Resolve(member.CkAttributeId.FullName, location,
                    operationResult);
            }

            // F1.1-S5: extends, association members and method value references.
            if (ckInterface.Extends != null)
            {
                ckInterface.Extends = ckInterface.Extends
                    .Select(i => (CkId<CkInterfaceId>)variableResolver.Resolve(i.FullName, location, operationResult))
                    .ToList();
            }

            foreach (var association in ckInterface.Associations ?? [])
            {
                association.CkRoleId = variableResolver.Resolve(association.CkRoleId.FullName, location,
                    operationResult);
                association.TargetCkTypeId = ResolveReference(association.TargetCkTypeId, variableResolver, location,
                    operationResult);
                association.TargetCkInterfaceId = ResolveReference(association.TargetCkInterfaceId, variableResolver,
                    location, operationResult);
            }

            ResolveMethodReferences(ckInterface.Methods, variableResolver, location, operationResult);

            ckModelGraph.GetOrCreateInterface(ckInterfaceId, ckInterface);
        }
    }

    /// <summary>
    ///     F1.2-S2 (review N5), mirroring the Secret rules 71: a Hidden assignment must not declare
    ///     <c>autoCompleteValues</c> (107 — the values are published in the model), and association-role attributes
    ///     cannot be Hidden (108 — association attributes have no access guard).
    /// </summary>
    private static void CheckHiddenAssignments(CkModelRootBase model, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        void CheckAutoComplete(object ckElementId, IEnumerable<CkTypeAttributeDto>? assignments)
        {
            foreach (var assignment in (assignments ?? []).Where(a =>
                         a.Access == CkAttributeAccessDto.Hidden && a.AutoCompleteValues is { Count: > 0 }))
            {
                operationResult.AddMessage(MessageCodes.HiddenAttributeAutoCompleteValues(
                    originFileResolver.Resolve(model.ModelId), assignment.AttributeName, ckElementId));
            }
        }

        foreach (var ckType in model.Types ?? [])
        {
            CheckAutoComplete(new CkId<CkTypeId>(model.ModelId, ckType.TypeId), ckType.Attributes);
        }

        foreach (var ckRecord in model.Records ?? [])
        {
            CheckAutoComplete(new CkId<CkRecordId>(model.ModelId, ckRecord.RecordId), ckRecord.Attributes);
        }

        foreach (var ckRole in model.AssociationRoles ?? [])
        {
            var ckRoleId = new CkId<CkAssociationRoleId>(model.ModelId, ckRole.AssociationRoleId);
            foreach (var assignment in (ckRole.Attributes ?? []).Where(a => a.Access == CkAttributeAccessDto.Hidden))
            {
                operationResult.AddMessage(MessageCodes.HiddenAttributeOnAssociationRole(
                    originFileResolver.Resolve(ckRoleId), ckRoleId, assignment.AttributeName));
            }
        }
    }

    private static void CheckGeneratedMethodNames(CkModelRootBase model, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ckType in model.Types ?? [])
        {
            foreach (var method in ckType.Methods ?? [])
            {
                var name = CkMethodIds.GeneratedName(ckType.TypeId, method.MethodId);
                var qualified = $"{ckType.TypeId.FullName}.{method.MethodId}";
                if (seen.TryGetValue(name, out var first))
                {
                    if (first != qualified)
                    {
                        operationResult.AddMessage(MessageCodes.CkMethodGeneratedNameCollision(
                            originFileResolver.Resolve(model.ModelId), first, qualified, model.ModelId, name));
                    }

                    continue;
                }

                seen.Add(name, qualified);
            }
        }
    }

    private static void ResolveMethodReferences(List<CkMethodDto>? methods, IVariableResolver variableResolver,
        string location, OperationResult operationResult)
    {
        foreach (var method in methods ?? [])
        {
            foreach (var parameter in method.Parameters ?? [])
            {
                parameter.ValueCkRecordId = ResolveReference(parameter.ValueCkRecordId, variableResolver, location,
                    operationResult);
                parameter.ValueCkEnumId = ResolveReference(parameter.ValueCkEnumId, variableResolver, location,
                    operationResult);
            }

            if (method.Result != null)
            {
                method.Result.ValueCkRecordId = ResolveReference(method.Result.ValueCkRecordId, variableResolver,
                    location, operationResult);
                method.Result.ValueCkEnumId = ResolveReference(method.Result.ValueCkEnumId, variableResolver,
                    location, operationResult);
            }
        }
    }

    private static CkId<TElementId>? ResolveReference<TElementId>(CkId<TElementId>? reference,
        IVariableResolver variableResolver, string location, OperationResult operationResult)
        where TElementId : IComparable<TElementId>, ICkElementId
    {
        return reference == null
            ? null
            : (CkId<TElementId>)variableResolver.Resolve(reference.FullName, location, operationResult);
    }
}