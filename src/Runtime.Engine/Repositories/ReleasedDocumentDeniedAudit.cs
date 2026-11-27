using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     The <c>Denied</c> entries of <c>System/DocumentAuditEntry</c> for writes the released-document lock refused: one
///     per refused document, with the document, the time (UTC), the caller's subject, the attributes the write would have
///     changed and the message number. <c>Source</c> stays empty: the engine does not know through which API path the
///     write came, and the write channel of a session is no such path.
/// </summary>
internal static class ReleasedDocumentDeniedAudit
{
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
        if (!DocumentAuditTrail.TryGetGraph(ckCacheService, tenantId, out auditGraph))
        {
            auditGraph = null;
            return [];
        }

        var entries = new List<RtEntity>(refusals.Count);
        foreach (var refusal in refusals)
        {
            entries.Add(DocumentAuditTrail.CreateEntry(auditGraph, caller, refusal.DocumentId,
                DocumentAuditTrail.DeniedAction, utcNow, new Dictionary<string, object?>
                {
                    [DocumentAuditTrail.AttributeNamesAttribute] = refusal.Changes.ToList(),
                    [DocumentAuditTrail.MessageAttribute] =
                        $"{RefusalMessageNumber}: {refusal.Operation} refused, the document is released " +
                        $"(state '{refusal.State}' of number range '{refusal.NumberRange}')"
                }));
        }

        return entries;
    }
}
