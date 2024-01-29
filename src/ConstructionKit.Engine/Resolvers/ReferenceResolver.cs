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
    /// <summary>
    ///     Resolves and checks cross reference within the model graph.
    /// </summary>
    /// <param name="modelGraph"></param>
    /// <param name="operationResult"></param>
    public void Resolve(CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        CheckCkAssociationRoles(modelGraph, operationResult);

        CheckCkAttributes(modelGraph, operationResult);

        CheckCkRecords(modelGraph, operationResult);

        CheckCkTypes(modelGraph, operationResult);
    }

    private static void CheckCkAssociationRoles(CkModelGraph modelGraph,
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
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(ckTypeAttribute.CkAttributeId, ckAssociationRoleKeyValue.Key));
                    continue;
                }

                ckAssociationRoleKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }
        }
    }

    private static void CheckCkRecords(CkModelGraph modelGraph,
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
                        MessageCodes.UnknownAttributeOfCkRecordIdInSource(ckTypeAttribute.CkAttributeId, ckRecordKeyValue.Key));
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
                        MessageCodes.UnknownDerivedFromCkRecordIdInSource(ckRecordKeyValue.Value.DerivedFromCkRecordId,
                            ckRecordKeyValue.Key));
                }
            }
        }
    }

    private static void CheckCkTypes(CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var ckTypeKeyValue in modelGraph.Types)
        {
            // Check 1.
            foreach (var ckTypeAttribute in ckTypeKeyValue.Value.DefinedAttributes)
            {
                if (!modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAttributeOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAttribute.CkAttributeId));
                    continue;
                }

                ckTypeKeyValue.Value.TryAddAttribute(new CkTypeAttributeGraph(ckTypeAttribute.CkAttributeId, ckTypeAttribute,
                    modelGraph.Attributes[ckTypeAttribute.CkAttributeId]));
            }

            // Check 2.
            if (ckTypeKeyValue.Value.DerivedFromCkTypeId != null)
            {
                if (!modelGraph.Types.ContainsKey(ckTypeKeyValue.Value.DerivedFromCkTypeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownCkDerivedIdOfCkTypeIdInSource(ckTypeKeyValue.Value.DerivedFromCkTypeId,
                            ckTypeKeyValue.Key));
                }
            }

            foreach (var ckTypeAssociation in ckTypeKeyValue.Value.Associations.DefinedAssociations)
            {
                // Check 3.
                if (!modelGraph.AssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownAssociationRoleOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.CkRoleId));
                }

                // Check 4.
                if (!modelGraph.Types.ContainsKey(ckTypeAssociation.TargetCkTypeId))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownTargetCkTypeIdOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.TargetCkTypeId));
                }
            }
        }
    }

    private static void CheckCkAttributes(CkModelGraph ckModelGraph, OperationResult operationResult)
    {
        foreach (var ckAttribute in ckModelGraph.Attributes)
        {
            if (ckAttribute.Value.ValueType == AttributeValueTypesDto.Record
                && ckAttribute.Value.ValueCkRecordId != null
                && !ckModelGraph.Records.ContainsKey(ckAttribute.Value.ValueCkRecordId))
            {
                operationResult.AddMessage(
                    MessageCodes.AttributeUsesUnknownCkRecordId(ckAttribute.Key,
                        ckAttribute.Value.ValueCkRecordId));
            }
        }
    }
}