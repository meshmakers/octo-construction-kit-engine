using System.Collections;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
///     A provided value for an attribute that only a CK method may write (<c>access: Hidden</c> or
///     <c>MethodOnly</c>).
/// </summary>
/// <param name="CkTypeId">The runtime CK type id of the entity as written in the file</param>
/// <param name="RtId">The rtId of the entity</param>
/// <param name="AttributePath">The attribute, or <c>Record.Member</c> / <c>Record[2].Member</c> for a record member</param>
internal readonly record struct RestrictedAttributeWrite(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId,
    string AttributePath);

/// <summary>
///     AB#7767 (findings H2/H3): the one implementation of "which of these entities carry a value for an attribute
///     that only a CK method may write". An RT import, a blueprint seed, a blueprint update and a migration Add step
///     never run a CK method, so none of them may set a <c>Hidden</c> or <c>MethodOnly</c> attribute — otherwise a
///     file or a blueprint could plant a password hash or overwrite a method-guarded value that GraphQL refuses.
///     <c>ReadOnly</c> stays importable. A pure read of the CK cache: it needs no repository and writes nothing,
///     so the callers can run it before the first write. The rule is
///     <see cref="CkTypeAttributeGraph.IsWritableOutsideMethod" />; record members are checked individually, like
///     the export checks them.
/// </summary>
internal static class RtAttributeAccessCheck
{
    /// <summary>
    ///     True when at least one type or record of the tenant's CK cache has an attribute that cannot be written
    ///     outside a method. Lets a caller skip work (for example the extra pass over a large import file) on tenants
    ///     without such an attribute.
    /// </summary>
    internal static bool TenantHasRestrictedAttribute(ICkCacheService ckCacheService, string tenantId)
    {
        return ckCacheService.GetCkTypes(tenantId).Any(t => HasRestricted(t)) ||
               ckCacheService.GetCkRecords(tenantId).Any(r => HasRestricted(r));
    }

    /// <summary>
    ///     Returns one offender per restricted attribute value found, in the order of the input. Empty when there is
    ///     none. An entity whose type is unknown to the cache is skipped here (the import reports it itself).
    /// </summary>
    internal static IReadOnlyList<RestrictedAttributeWrite> Find(ICkCacheService ckCacheService, string tenantId,
        IEnumerable<RtEntityTcDto> entities)
    {
        var offenders = new List<RestrictedAttributeWrite>();
        foreach (var entity in entities)
        {
            if (entity.CkTypeId == null ||
                !ckCacheService.TryGetRtCkType(tenantId, entity.CkTypeId, out var typeGraph) || typeGraph == null)
            {
                continue;
            }

            Collect(ckCacheService, tenantId, typeGraph, entity, prefix: string.Empty,
                path => offenders.Add(new RestrictedAttributeWrite(entity.CkTypeId, entity.RtId, path)));
        }

        return offenders;
    }

    /// <summary>The refusal message (one code for every path; parameters rtId, ckTypeId, attributePath).</summary>
    internal static OperationMessage ToMessage(RestrictedAttributeWrite offender) =>
        MessageCodes.RtAttributeWriteRestricted($"{offender.CkTypeId.SemanticVersionedFullName}@{offender.RtId}",
            offender.RtId, offender.CkTypeId.SemanticVersionedFullName, offender.AttributePath);

    private static bool HasRestricted(CkTypeWithAttributesGraph graph) =>
        graph.AllAttributes.Values.Any(a => !a.IsWritableOutsideMethod);

    private static void Collect(ICkCacheService ckCacheService, string tenantId, CkTypeWithAttributesGraph graph,
        RtTypeWithAttributesTcDto dto, string prefix, Action<string> report)
    {
        foreach (var modelAttribute in dto.Attributes)
        {
            var attribute = graph.AllAttributes.Values.FirstOrDefault(a => a.CkAttributeId.Equals(modelAttribute.Id));
            if (attribute == null)
            {
                continue; // unknown attribute: the import fails on it with its own message
            }

            var path = prefix + attribute.AttributeName;
            if (!attribute.IsWritableOutsideMethod)
            {
                report(path);
                continue;
            }

            switch (modelAttribute.Value)
            {
                case RtRecordTcDto record:
                    CollectRecord(ckCacheService, tenantId, record, path, report);
                    break;
                case IEnumerable items and not string:
                    var index = 0;
                    foreach (var item in items)
                    {
                        if (item is RtRecordTcDto listRecord)
                        {
                            CollectRecord(ckCacheService, tenantId, listRecord, $"{path}[{index}]", report);
                        }

                        index++;
                    }

                    break;
            }
        }
    }

    private static void CollectRecord(ICkCacheService ckCacheService, string tenantId, RtRecordTcDto record,
        string path, Action<string> report)
    {
        if (ckCacheService.TryGetRtCkRecord(tenantId, record.CkRecordId, out var recordGraph) && recordGraph != null)
        {
            Collect(ckCacheService, tenantId, recordGraph, record, path + ".", report);
        }
    }
}
