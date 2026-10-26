using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Compile-time rule of message 138 (AB#7467, follow-up of AB#7446): a record-valued attribute that is part of
///     <c>ExportRt</c> must not carry a required member that the export drops.
/// </summary>
/// <remarks>
///     <para>
///         <c>RtEntityToTcDtoConverter.ConvertAttributes</c> evaluates the members of an exported record
///         individually against their own effective ownership. A required member without a default value whose
///         ownership is excluded from export (<c>RuntimeState</c>, <c>Secret</c>) is therefore missing from the
///         exported record, and a re-import cannot rebuild a valid record (AB#7450). The compiler cannot see seed
///         data (that lives in blueprints); the detectable defect is the ownership mismatch between the
///         record-valued attribute and the record's members.
///     </para>
///     <para>
///         Scope: every attribute assignment of a type or record of the model being compiled whose value type is
///         <c>Record</c> / <c>RecordArray</c> and whose effective ownership is exported (<c>SeedOwned</c>,
///         <c>TenantOwned</c>). The referenced record is walked (also when a dependency model declares it), including
///         nested records reached through further exported record-valued members. Not reported: optional members,
///         members with default values, and everything below a record-valued attribute or member that is itself
///         excluded from export (it is dropped as one unit). Records that are not reachable from an assignment of the
///         compiled model are reported by the model that uses them. A warning only, so existing models compile
///         unchanged.
///     </para>
/// </remarks>
internal static class ExportedRecordMemberValidator
{
    public static void Validate(CkModelRootBase model, CkModelGraph modelGraph,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var modelId = model.ModelId;

        foreach (var type in model.Types ?? [])
        {
            var ckTypeId = new CkId<CkTypeId>(modelId, type.TypeId);
            if (!modelGraph.Types.TryGetValue(ckTypeId, out var typeGraph))
            {
                continue;
            }

            ValidateAssignments(ckTypeId.ToString(), originFileResolver.Resolve(ckTypeId), type.Attributes,
                typeGraph, modelGraph, operationResult);
        }

        foreach (var record in model.Records ?? [])
        {
            var ckRecordId = new CkId<CkRecordId>(modelId, record.RecordId);
            if (!modelGraph.Records.TryGetValue(ckRecordId, out var recordGraph))
            {
                continue;
            }

            ValidateAssignments(ckRecordId.ToString(), originFileResolver.Resolve(ckRecordId), record.Attributes,
                recordGraph, modelGraph, operationResult);
        }
    }

    private static void ValidateAssignments(string ckElementId, string location,
        IEnumerable<CkTypeAttributeDto>? assignments, CkTypeWithAttributesGraph owner, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var assignment in assignments ?? [])
        {
            // The graph holds the effective values (assignment override over definition).
            if (!owner.AllAttributes.TryGetValue(assignment.CkAttributeId, out var attribute) ||
                !IsRecordValued(attribute) ||
                attribute.Ownership.IsExcludedFromExport() ||
                !modelGraph.Records.TryGetValue(attribute.ValueCkRecordId!, out var recordGraph))
            {
                continue;
            }

            WalkRecord(ckElementId, location, attribute.AttributeName, attribute.AttributeName, recordGraph,
                [recordGraph.CkRecordId], modelGraph, operationResult);
        }
    }

    private static void WalkRecord(string ckElementId, string location, string attributeName, string pathPrefix,
        CkRecordGraph recordGraph, HashSet<CkId<CkRecordId>> walking, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var member in recordGraph.AllAttributesByName.Values.OrderBy(m => m.AttributeName,
                     StringComparer.Ordinal))
        {
            var memberPath = $"{pathPrefix}.{member.AttributeName}";

            if (member.Ownership.IsExcludedFromExport())
            {
                if (!member.IsOptional && member.DefaultValues is not { Count: > 0 })
                {
                    operationResult.AddMessage(MessageCodes.ExportedRecordDropsRequiredMember(location, ckElementId,
                        attributeName, recordGraph.CkRecordId, memberPath, member.Ownership));
                }

                // An excluded member is dropped as one unit - nothing below it is exported.
                continue;
            }

            if (IsRecordValued(member) &&
                modelGraph.Records.TryGetValue(member.ValueCkRecordId!, out var nested) &&
                walking.Add(nested.CkRecordId))
            {
                WalkRecord(ckElementId, location, attributeName, memberPath, nested, walking, modelGraph,
                    operationResult);
                walking.Remove(nested.CkRecordId);
            }
        }
    }

    private static bool IsRecordValued(CkTypeAttributeGraph attribute)
    {
        return attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray &&
               attribute.ValueCkRecordId != null;
    }
}
