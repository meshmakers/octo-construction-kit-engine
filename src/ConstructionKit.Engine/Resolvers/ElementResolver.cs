using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

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

                foreach (var method in ckType.Methods ?? [])
                {
                    foreach (var parameter in method.Parameters ?? [])
                    {
                        parameter.ValueCkRecordId = ResolveReference(parameter.ValueCkRecordId, variableResolver,
                            originFileResolver.Resolve(ckTypeId), operationResult);
                        parameter.ValueCkEnumId = ResolveReference(parameter.ValueCkEnumId, variableResolver,
                            originFileResolver.Resolve(ckTypeId), operationResult);
                    }

                    if (method.Result != null)
                    {
                        method.Result.ValueCkRecordId = ResolveReference(method.Result.ValueCkRecordId,
                            variableResolver, originFileResolver.Resolve(ckTypeId), operationResult);
                        method.Result.ValueCkEnumId = ResolveReference(method.Result.ValueCkEnumId, variableResolver,
                            originFileResolver.Resolve(ckTypeId), operationResult);
                    }
                }

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
    }

    /// <summary>
    ///     CK v2 (AB#5584): message 91 for a CK language version this engine does not support, message 90 for
    ///     every CK v2 feature used by a model that does not declare <c>ckLanguage: 2</c>.
    /// </summary>
    private static void CheckCkLanguage(CkModelRootBase model, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var location = originFileResolver.Resolve(model.ModelId);
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

            ckModelGraph.GetOrCreateInterface(ckInterfaceId, ckInterface);
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