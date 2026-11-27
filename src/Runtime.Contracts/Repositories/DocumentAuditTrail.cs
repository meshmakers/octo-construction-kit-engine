using System.Diagnostics.CodeAnalysis;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     The entries of the document audit trail (<c>System/DocumentAuditEntry</c>, design note
///     <c>docs/concept-number-ranges-and-released-documents.md</c>, 1.5 and D9) that the engine writes itself: <c>Release</c>
///     when a document gets its number on release, <c>Storno</c> on the original and on the storno when a storno is
///     released, <c>Denied</c> when the released-document lock refuses a write. Every other insert of an entry is refused
///     by the append-only lock, so this is the only way an entry is created: <see cref="InsertAsync" /> writes straight into
///     the collection, past the write funnel and its lock, and it is internal, so no API reaches it.
/// </summary>
internal static class DocumentAuditTrail
{
    /// <summary>Version-less id of <c>System/DocumentAuditEntry</c>.</summary>
    internal static readonly RtCkId<CkTypeId> CkTypeId = new("System", "DocumentAuditEntry");

    /// <summary>Key of <c>DocumentAuditActions.Release</c>.</summary>
    internal const int ReleaseAction = 0;

    /// <summary>Key of <c>DocumentAuditActions.Storno</c>.</summary>
    internal const int StornoAction = 1;

    /// <summary>Key of <c>DocumentAuditActions.Denied</c>.</summary>
    internal const int DeniedAction = 2;

    internal const string DocumentRtIdAttribute = "DocumentRtId";
    internal const string DocumentCkTypeIdAttribute = "DocumentCkTypeId";
    internal const string ActionAttribute = "Action";
    internal const string TimestampAttribute = "Timestamp";
    internal const string SubjectIdAttribute = "SubjectId";
    internal const string DocumentNumberAttribute = "DocumentNumber";
    internal const string FiscalYearAttribute = "FiscalYear";
    internal const string SequenceValueAttribute = "SequenceValue";
    internal const string ReferenceRtIdAttribute = "ReferenceRtId";
    internal const string AttributeNamesAttribute = "AttributeNames";
    internal const string MessageAttribute = "Message";

    /// <summary>Whether <paramref name="ckTypeId" /> is <c>System/DocumentAuditEntry</c> (any version).</summary>
    public static bool IsAuditEntryType(RtCkId<CkTypeId>? ckTypeId) =>
        ckTypeId != null && string.Equals(ckTypeId.FullName, CkTypeId.FullName, StringComparison.Ordinal);

    /// <summary>The type of the entries in the tenant; false when the tenant's System model has none.</summary>
    public static bool TryGetGraph(ICkCacheService ckCacheService, string tenantId,
        [NotNullWhen(true)] out CkTypeGraph? auditGraph) =>
        ckCacheService.TryGetRtCkType(tenantId, CkTypeId, out auditGraph);

    /// <summary>
    ///     One entry about <paramref name="document" />: the action, the time (UTC) and the caller's subject (a user's
    ///     subject or a client's id; none for a system session), plus the <paramref name="details" /> of the action.
    /// </summary>
    public static RtEntity CreateEntry(CkTypeGraph auditGraph, RtSecurityContext caller, RtEntityId document,
        int action, DateTime utcNow, IReadOnlyDictionary<string, object?>? details = null)
    {
        var timestamp = DateTime.SpecifyKind(utcNow.ToUniversalTime(), DateTimeKind.Utc);
        var attributes = new Dictionary<string, object?>
        {
            [DocumentRtIdAttribute] = document.RtId.ToString(),
            [DocumentCkTypeIdAttribute] = document.CkTypeId.FullName,
            [ActionAttribute] = action,
            [TimestampAttribute] = timestamp
        };
        var subjectId = caller.IsSystem || string.IsNullOrEmpty(caller.SubjectId) ? null : caller.SubjectId;
        if (subjectId != null)
        {
            attributes[SubjectIdAttribute] = subjectId;
        }

        if (details != null)
        {
            foreach (var (name, value) in details.Where(d => d.Value != null))
            {
                attributes[name] = value;
            }
        }

        return new RtEntity(auditGraph.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId(), attributes)
        {
            RtCreationDateTime = timestamp,
            RtChangedDateTime = timestamp,
            RtCreatedBy = subjectId
        };
    }

    /// <summary>
    ///     Writes <paramref name="entries" /> in <paramref name="session" />: inside a transaction they are part of it, so
    ///     an abort leaves no entry.
    /// </summary>
    public static async Task InsertAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        CkTypeGraph auditGraph, IReadOnlyList<RtEntity> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await repositoryDataSource.GetRtCollection<RtEntity>(auditGraph).InsertManyAsync(session, entries)
            .ConfigureAwait(false);
    }
}
