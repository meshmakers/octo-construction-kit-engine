using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     The <c>Denied</c> entries of <c>System/DocumentAuditEntry</c> (System 2.9.0) for writes the released-document lock
///     refused: one per refused document, with the document, the time (UTC), the caller's subject and write channel, the
///     attributes the write would have changed and the message number. Only what a refusal needs; the release and storno
///     entries and the read rules of the audit trail are not built here.
/// </summary>
internal static class ReleasedDocumentDeniedAudit
{
    /// <summary>Version-less id of <c>System/DocumentAuditEntry</c>.</summary>
    internal static readonly RtCkId<CkTypeId> DocumentAuditEntryCkTypeId = new("System", "DocumentAuditEntry");

    /// <summary>Key of <c>DocumentAuditActions.Denied</c>.</summary>
    internal const int DeniedAction = 2;

    /// <summary>Engine message number of the refusal, written into <c>Message</c>.</summary>
    internal const int RefusalMessageNumber = 168;

    /// <summary>
    ///     The audit entries of <paramref name="refusals" />; <paramref name="auditGraph" /> is null (and the list empty)
    ///     when the tenant has no <c>System/DocumentAuditEntry</c> type.
    /// </summary>
    public static List<RtEntity> CreateEntries(ICkCacheService ckCacheService, string tenantId,
        RtSecurityContext caller, IReadOnlyList<ReleasedDocumentRefusal> refusals, DateTime utcNow,
        out CkTypeGraph? auditGraph)
    {
        if (!ckCacheService.TryGetRtCkType(tenantId, DocumentAuditEntryCkTypeId, out auditGraph))
        {
            auditGraph = null;
            return [];
        }

        var ckTypeId = auditGraph.CkTypeId.ToRtCkId();
        var entries = new List<RtEntity>(refusals.Count);
        foreach (var refusal in refusals)
        {
            var attributes = new Dictionary<string, object?>
            {
                ["DocumentRtId"] = refusal.DocumentId.RtId.ToString(),
                ["DocumentCkTypeId"] = refusal.DocumentId.CkTypeId.FullName,
                ["Action"] = DeniedAction,
                ["Timestamp"] = utcNow,
                ["AttributeNames"] = refusal.Changes.ToList(),
                ["Message"] = $"{RefusalMessageNumber}: {refusal.Operation} refused, the document is released " +
                              $"(state '{refusal.State}' of number range '{refusal.NumberRange}')"
            };
            if (!caller.IsSystem && !string.IsNullOrEmpty(caller.SubjectId))
            {
                attributes["SubjectId"] = caller.SubjectId;
            }

            if (!string.IsNullOrEmpty(caller.WriteChannel))
            {
                attributes["Source"] = caller.WriteChannel;
            }

            entries.Add(new RtEntity(ckTypeId, OctoObjectId.GenerateNewId(), attributes)
            {
                RtCreationDateTime = utcNow,
                RtChangedDateTime = utcNow,
                RtCreatedBy = caller.IsSystem ? null : caller.SubjectId
            });
        }

        return entries;
    }
}
