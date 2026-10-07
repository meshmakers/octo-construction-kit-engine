using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Resolves and checks references of the model. E. g. derived types, records or cross references
/// </summary>
internal class ReferenceResolver : IReferenceResolver
{
    public void Resolve(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        CheckCkAssociationRoles(modelGraph, originFileResolver, operationResult);

        CheckCkAttributes(modelGraph, originFileResolver, operationResult);

        CheckCkRecords(modelGraph, originFileResolver, operationResult);

        CheckCkInterfaces(modelGraph, originFileResolver, operationResult);

        CheckCkTypes(modelGraph, originFileResolver, operationResult);
    }

    /// <summary>
    ///     CK v2 (AB#5667): every interface member references an existing attribute (94); the member is merged with
    ///     the attribute definition into <see cref="CkInterfaceGraph.Attributes" />.
    /// </summary>
    private static void CheckCkInterfaces(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var ckInterface in modelGraph.Interfaces.Values)
        {
            foreach (var member in ckInterface.DefinedAttributes)
            {
                if (!modelGraph.Attributes.TryGetValue(member.CkAttributeId, out var attributeGraph))
                {
                    operationResult.AddMessage(MessageCodes.CkInterfaceAttributeUnknown(
                        originFileResolver.Resolve(ckInterface.CkInterfaceId), member.AttributeName,
                        ckInterface.CkInterfaceId, member.CkAttributeId));
                    continue;
                }

                ckInterface.TryAddAttribute(new CkTypeAttributeGraph(member.CkAttributeId,
                    new CkTypeAttributeDto
                    {
                        CkAttributeId = member.CkAttributeId, AttributeName = member.AttributeName,
                        IsOptional = member.IsOptional
                    }, attributeGraph));
            }
        }
    }

    /// <summary>
    ///     CK v2 (AB#5667 / AB#5669): <c>implements</c> entries exist (95); record and enum references of method
    ///     parameters and results exist (101).
    /// </summary>
    private static void CheckCkV2TypeReferences(CkModelGraph modelGraph, CkId<CkTypeId> ckId, CkTypeGraph ckTypeGraph,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        foreach (var ckInterfaceId in ckTypeGraph.DeclaredImplements)
        {
            if (!modelGraph.Interfaces.ContainsKey(ckInterfaceId))
            {
                operationResult.AddMessage(MessageCodes.ImplementsUnknownCkInterface(originFileResolver.Resolve(ckId),
                    ckId, ckInterfaceId));
            }
        }

        foreach (var method in ckTypeGraph.DefinedMethods)
        {
            void CheckValue(string what, CkId<CkRecordId>? recordId, CkId<CkEnumId>? enumId)
            {
                if (recordId != null && !modelGraph.Records.ContainsKey(recordId))
                {
                    operationResult.AddMessage(MessageCodes.CkMethodParameterInvalid(originFileResolver.Resolve(ckId),
                        method.MethodId, ckId, $"{what} references unknown record '{recordId}'"));
                }

                if (enumId != null && !modelGraph.Enums.ContainsKey(enumId))
                {
                    operationResult.AddMessage(MessageCodes.CkMethodParameterInvalid(originFileResolver.Resolve(ckId),
                        method.MethodId, ckId, $"{what} references unknown enum '{enumId}'"));
                }
            }

            foreach (var parameter in method.Parameters ?? [])
            {
                CheckValue($"parameter '{parameter.Name}'", parameter.ValueCkRecordId, parameter.ValueCkEnumId);
            }

            if (method.Result != null)
            {
                CheckValue("the result", method.Result.ValueCkRecordId, method.Result.ValueCkEnumId);
            }
        }
    }

    private static void CheckCkAssociationRoles(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var ckAssociationRoleKeyValue in modelGraph.AssociationRoles)
        {
            // Check 1.
            foreach (var ckTypeAttribute in ckAssociationRoleKeyValue.Value.DefinedAttributes)
            {
                if (!modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(
                            originFileResolver.Resolve(ckAssociationRoleKeyValue.Key),
                            ckTypeAttribute.CkAttributeId, ckAssociationRoleKeyValue.Key));
                    continue;
                }

                ckAssociationRoleKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId,
                    ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }
        }
    }

    private static void CheckCkRecords(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var ckRecordKeyValue in modelGraph.Records)
        {
            // Check 1.
            foreach (var ckTypeAttribute in ckRecordKeyValue.Value.DefinedAttributes)
            {
                if (!modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(
                            originFileResolver.Resolve(ckRecordKeyValue.Key),
                            ckTypeAttribute.CkAttributeId, ckRecordKeyValue.Key));
                    continue;
                }

                ckRecordKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId,
                    ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }

            // Check 2.
            if (ckRecordKeyValue.Value.DerivedFromCkRecordId != null)
            {
                if (!modelGraph.Records.ContainsKey(ckRecordKeyValue.Value.DerivedFromCkRecordId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownDerivedFromCkRecordIdInSource(
                            originFileResolver.Resolve(ckRecordKeyValue.Key),
                            ckRecordKeyValue.Value.DerivedFromCkRecordId,
                            ckRecordKeyValue.Key));
                }
            }
        }
    }

    private static void CheckCkTypes(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var f in modelGraph.Types)
        {
            var ckId = f.Key;
            var ckTypeGraph = f.Value;
            CheckCkV2TypeReferences(modelGraph, ckId, ckTypeGraph, originFileResolver, operationResult);

            // Check 1.
            foreach (var ckTypeAttribute in ckTypeGraph.DefinedAttributes)
            {
                if (!modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAttributeOfCkTypeIdInSource(originFileResolver.Resolve(ckId),
                            ckTypeAttribute.CkAttributeId, ckId));
                    continue;
                }

                var ckAttribute = modelGraph.Attributes[ckTypeAttribute.CkAttributeId];

                ckTypeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    ckAttribute));
            }

            // Check 2.
            if (ckTypeGraph.DerivedFromCkTypeId != null)
            {
                if (!modelGraph.Types.ContainsKey(ckTypeGraph.DerivedFromCkTypeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownCkDerivedIdOfCkTypeIdInSource(originFileResolver.Resolve(ckId),
                            ckTypeGraph.DerivedFromCkTypeId, ckId));
                }
            }

            foreach (var ckTypeAssociation in ckTypeGraph.Associations.DefinedAssociations)
            {
                // Check 3.
                if (!modelGraph.AssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAssociationRoleOfCkTypeIdInSource(originFileResolver.Resolve(ckId),
                            ckId, ckTypeAssociation.CkRoleId));
                }

                // Check 4.
                if (!modelGraph.Types.ContainsKey(ckTypeAssociation.TargetCkTypeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownTargetCkTypeIdOfCkTypeIdInSource(originFileResolver.Resolve(ckId),
                            ckId, ckTypeAssociation.TargetCkTypeId));
                }
            }
        }
    }

    private static void CheckCkAttributes(CkModelGraph ckModelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var ckAttribute in ckModelGraph.Attributes)
        {
            switch (ckAttribute.Value.ValueType)
            {
                case AttributeValueTypesDto.Record:
                case AttributeValueTypesDto.RecordArray:
                    
                    if (ckAttribute.Value.ValueCkRecordId == null)
                    {
                        operationResult.AddMessage(
                            MessageCodes.AttributeIsRecordButValueIsNotSet(
                                originFileResolver.Resolve(ckAttribute.Value.CkAttributeId),
                                ckAttribute.Key));
                    }
                    else if (ckAttribute.Value.ValueCkRecordId != null
                        && !ckModelGraph.Records.ContainsKey(ckAttribute.Value.ValueCkRecordId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.AttributeUsesUnknownCkRecordId(
                                originFileResolver.Resolve(ckAttribute.Value.CkAttributeId),
                                ckAttribute.Key,
                                ckAttribute.Value.ValueCkRecordId));
                    }
                    
                    break;
                case AttributeValueTypesDto.Enum:

                    if (ckAttribute.Value.ValueCkEnumId == null)
                    {
                        operationResult.AddMessage(
                            MessageCodes.AttributeIsEnumButValueIsNotSet(
                                originFileResolver.Resolve(ckAttribute.Value.CkAttributeId),
                                ckAttribute.Key));
                    }
                    else if (ckAttribute.Value.ValueCkEnumId != null
                        && !ckModelGraph.Enums.ContainsKey(ckAttribute.Value.ValueCkEnumId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.AttributeUsesUnknownCkEnumId(
                                originFileResolver.Resolve(ckAttribute.Value.CkAttributeId),
                                ckAttribute.Key,
                                ckAttribute.Value.ValueCkEnumId));
                    }
                    break;
                // there is no default case here, because we only check specific types
            }
        }
    }
}