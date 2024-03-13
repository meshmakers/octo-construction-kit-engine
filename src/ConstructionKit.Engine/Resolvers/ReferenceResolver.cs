using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
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

        CheckCkTypes(modelGraph, originFileResolver, operationResult);
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
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(originFileResolver.Resolve(ckAssociationRoleKeyValue.Key),
                            ckTypeAttribute.CkAttributeId, ckAssociationRoleKeyValue.Key));
                    continue;
                }

                ckAssociationRoleKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
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
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(originFileResolver.Resolve(ckRecordKeyValue.Key),
                            ckTypeAttribute.CkAttributeId, ckRecordKeyValue.Key));
                    continue;
                }

                ckRecordKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }

            // Check 2.
            if (ckRecordKeyValue.Value.DerivedFromCkRecordId != null)
            {
                if (!modelGraph.Records.ContainsKey(ckRecordKeyValue.Value.DerivedFromCkRecordId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownDerivedFromCkRecordIdInSource(originFileResolver.Resolve(ckRecordKeyValue.Key),
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
                ckTypeGraph.IsStreamType |= ckAttribute.IsDataStream;
                
                ckTypeGraph.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute, ckAttribute));
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
            if ((ckAttribute.Value.ValueType == AttributeValueTypesDto.Record ||
                 ckAttribute.Value.ValueType == AttributeValueTypesDto.RecordArray)
                && ckAttribute.Value.ValueCkRecordId != null
                && !ckModelGraph.Records.ContainsKey(ckAttribute.Value.ValueCkRecordId))
            {
                operationResult.AddMessage(
                    MessageCodes.AttributeUsesUnknownCkRecordId(originFileResolver.Resolve(ckAttribute.Value.CkAttributeId),
                        ckAttribute.Key,
                        ckAttribute.Value.ValueCkRecordId));
            }
        }
    }
}