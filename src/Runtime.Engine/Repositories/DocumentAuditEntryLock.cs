using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Decision D9 of the design note <c>docs/concept-number-ranges-and-released-documents.md</c>: entries of the document
///     audit trail (<c>System/DocumentAuditEntry</c>) are append-only, and only the engine creates them. Every insert,
///     update, replace, delete and archive of an entry through the write funnel is refused, and so is an import of an
///     entry the tenant does not hold or one that changes a stored entry; an import that carries a stored entry unchanged
///     is a no-op (the entry is not written). Called with the released-document lock from <see cref="BulkRtMutation" />
///     and the bulk import, so no option and no identity (also no system session) skips it. The engine writes its own
///     entries past it, straight into the collection (<see cref="DocumentAuditTrail" />). A tenant restore stays outside
///     (D10).
/// </summary>
internal static class DocumentAuditEntryLock
{
    /// <summary>Refuses the changes of one write batch that create or touch an audit entry.</summary>
    public static void CheckMutation(EntityRuleEngineResult<RtEntity> entities)
    {
        var result = new OperationResult();
        foreach (var id in entities.RtEntitiesToDelete.Where(IsAuditEntry))
        {
            Refuse(result, id, ReleasedDocumentLock.OperationDelete);
        }

        foreach (var id in entities.RtEntitiesToUpdate.Keys.Where(IsAuditEntry))
        {
            Refuse(result, id, ReleasedDocumentLock.OperationUpdate);
        }

        foreach (var id in entities.RtEntitiesToReplace.Keys.Where(IsAuditEntry))
        {
            Refuse(result, id, ReleasedDocumentLock.OperationReplace);
        }

        foreach (var entity in entities.RtEntitiesToInsert.Where(e => DocumentAuditTrail.IsAuditEntryType(e.GetRtCkTypeId())))
        {
            Refuse(result, new RtEntityId(entity.GetRtCkTypeId(), entity.RtId), ReleasedDocumentLock.OperationInsert);
        }

        ThrowIfAny(result);
    }

    /// <summary>
    ///     The entities of a bulk import that are written: an audit entry that carries a stored entry unchanged is left
    ///     out, every other audit entry (a new one, or one that changes a stored entry) is refused; every other entity is
    ///     written as given.
    /// </summary>
    public static async Task<IReadOnlyList<RtEntity>> FilterImportAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService, IReadOnlyList<RtEntity> entities)
    {
        var auditEntries = entities.Where(e => DocumentAuditTrail.IsAuditEntryType(e.GetRtCkTypeId())).ToList();
        if (auditEntries.Count == 0)
        {
            return entities;
        }

        var stored = await ReadStoredAsync(session, repositoryDataSource, ckCacheService, auditEntries)
            .ConfigureAwait(false);
        var result = new OperationResult();
        foreach (var entity in auditEntries)
        {
            var unchanged = entity.RtId != OctoObjectId.Empty && stored.TryGetValue(entity.RtId, out var storedEntry) &&
                            storedEntry.Attributes.Keys.Union(entity.Attributes.Keys, StringComparer.Ordinal)
                                .All(name => RtAttributeValueEquality.AreEqual(entity.GetAttributeValueOrDefault(name),
                                    storedEntry.GetAttributeValueOrDefault(name)));
            if (!unchanged)
            {
                Refuse(result, new RtEntityId(entity.GetRtCkTypeId(), entity.RtId), ReleasedDocumentLock.OperationImport);
            }
        }

        ThrowIfAny(result);
        return entities.Where(e => !DocumentAuditTrail.IsAuditEntryType(e.GetRtCkTypeId())).ToList();
    }

    private static bool IsAuditEntry(RtEntityId id) => DocumentAuditTrail.IsAuditEntryType(id.CkTypeId);

    private static async Task<Dictionary<OctoObjectId, RtEntity>> ReadStoredAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService, IReadOnlyList<RtEntity> entries)
    {
        if (!DocumentAuditTrail.TryGetGraph(ckCacheService, repositoryDataSource.TenantId, out var graph))
        {
            return [];
        }

        var ids = entries.Select(e => e.RtId).Where(id => id != OctoObjectId.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return (await repositoryDataSource.GetRtCollection<RtEntity>(graph)
                .FindManyAsync(session, e => ids.Contains(e.RtId)).ConfigureAwait(false))
            .ToDictionary(e => e.RtId);
    }

    private static void Refuse(OperationResult result, RtEntityId id, string operation)
    {
        var ckTypeId = id.CkTypeId.FullName;
        result.AddMessage(MessageCodes.RtDocumentAuditEntryAppendOnly($"{ckTypeId}@{id.RtId}", id.RtId, ckTypeId,
            operation));
    }

    private static void ThrowIfAny(OperationResult result)
    {
        if (result.HasErrors || result.HasFatalErrors)
        {
            throw new DocumentAuditEntryAppendOnlyException(result);
        }
    }
}
