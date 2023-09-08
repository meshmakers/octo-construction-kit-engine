using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Resolves and checks references of the model. E. g. derived types, records or cross references
/// </summary>
public class ReferenceResolver : IReferenceResolver
{
    /// <summary>
    /// Resolves and checks cross reference within the model graph.
    /// </summary>
    /// <param name="aggregatedModelElements"></param>
    /// <param name="modelGraph"></param>
    /// <param name="operationResult"></param>
    public void Resolve(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        CheckCkAttributes(aggregatedModelElements, operationResult);

        CheckCkRecords(aggregatedModelElements, modelGraph, operationResult);

        CheckCkTypes(aggregatedModelElements, modelGraph, operationResult);
    }

    private static void CheckCkRecords(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var ckRecordKeyValue in aggregatedModelElements.CkRecords)
        {
            // Check 1.
            if (ckRecordKeyValue.Value.Attributes != null)
            {
                foreach (var ckTypeAttribute in ckRecordKeyValue.Value.Attributes)
                {
                    if (!aggregatedModelElements.CkAttributes.ContainsKey(ckTypeAttribute.CkAttributeId) &&
                        !modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownAttributeOfCkRecordIdInSource(ckRecordKeyValue.Key, ckTypeAttribute.CkAttributeId));
                    }
                }
            }

            // Check 2.
            if (ckRecordKeyValue.Value.DerivedFromCkRecordId != null)
            {
                if (!aggregatedModelElements.CkRecords.ContainsKey(ckRecordKeyValue.Value.DerivedFromCkRecordId.Value) &&
                    !modelGraph.Records.ContainsKey(ckRecordKeyValue.Value.DerivedFromCkRecordId.Value))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownDerivedFromCkRecordIdInSource(ckRecordKeyValue.Value.DerivedFromCkRecordId,
                            ckRecordKeyValue.Key));
                }
            }
        }
    }

    private static void CheckCkTypes(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var ckTypeKeyValue in aggregatedModelElements.CkTypes)
        {
            // Check 1.
            if (ckTypeKeyValue.Value.Attributes != null)
            {
                foreach (var ckTypeAttribute in ckTypeKeyValue.Value.Attributes)
                {
                    if (!aggregatedModelElements.CkAttributes.ContainsKey(ckTypeAttribute.CkAttributeId) &&
                        !modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownAttributeOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAttribute.CkAttributeId));
                    }
                }
            }

            // Check 2.
            if (ckTypeKeyValue.Value.DerivedFromCkTypeId != null)
            {
                if (!aggregatedModelElements.CkTypes.ContainsKey(ckTypeKeyValue.Value.DerivedFromCkTypeId.Value) &&
                    !modelGraph.Types.ContainsKey(ckTypeKeyValue.Value.DerivedFromCkTypeId.Value))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownCkDerivedIdOfCkTypeIdInSource(ckTypeKeyValue.Value.DerivedFromCkTypeId,
                            ckTypeKeyValue.Key));
                }
            }

            if (ckTypeKeyValue.Value.Associations != null)
            {
                foreach (var ckTypeAssociation in ckTypeKeyValue.Value.Associations)
                {
                    // Check 3.
                    if (!aggregatedModelElements.CkAssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId) &&
                        !modelGraph.AssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownAssociationRoleOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.CkRoleId));
                    }

                    // Check 4.
                    if (!aggregatedModelElements.CkTypes.ContainsKey(ckTypeAssociation.TargetCkTypeId) &&
                        !modelGraph.Types.ContainsKey(ckTypeAssociation.TargetCkTypeId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownTargetCkTypeIdOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.TargetCkTypeId));
                    }
                }
            }
        }
    }

    private static void CheckCkAttributes(CkAggregatedModelElements aggregatedModelElements, OperationResult operationResult)
    {
        foreach (var ckAttribute in aggregatedModelElements.CkAttributes)
        {
            if (ckAttribute.Value.ValueType == AttributeValueTypesDto.Record
                && ckAttribute.Value.ValueCkRecordId != null
                && !aggregatedModelElements.CkRecords.ContainsKey(ckAttribute.Value.ValueCkRecordId.Value))
            {
                operationResult.AddMessage(
                    MessageCodes.AttributeUsesUnknownCkRecordId(ckAttribute.Key,
                        ckAttribute.Value.ValueCkRecordId.Value));
            }
        }
    }
}