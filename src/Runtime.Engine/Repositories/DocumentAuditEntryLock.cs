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
///     audit trail (<c>System/DocumentAuditEntry</c>) are append-only. Every update, replace, delete and archive of an
///     entry is refused, and so is an insert or an import over a stored entry - an import that carries the stored content
///     is a no-op (the entry is not written). New entries are written. Called with the released-document lock from
///     <see cref="BulkRtMutation" /> and the bulk import, so no option and no identity skips it. A tenant restore stays
///     outside (D10).
/// </summary>
internal static class DocumentAuditEntryLock
{
    /// <summary>Refuses the changes of one write batch that touch a stored audit entry.</summary>
    public static async Task CheckMutationAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService, EntityRuleEngineResult<RtEntity> entities)
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

        var inserted = entities.RtEntitiesToInsert
            .Where(e => e.RtId != OctoObjectId.Empty && IsAuditEntry(new RtEntityId(e.GetRtCkTypeId(), e.RtId)))
            .ToList();
        if (inserted.Count > 0)
        {
            var stored = await ReadStoredAsync(session, repositoryDataSource, ckCacheService, inserted)
                .ConfigureAwait(false);
            foreach (var entity in inserted.Where(e => stored.ContainsKey(e.RtId)))
            {
                Refuse(result, new RtEntityId(entity.GetRtCkTypeId(), entity.RtId), ReleasedDocumentLock.OperationInsert);
            }
        }

        ThrowIfAny(result);
    }

    /// <summary>
    ///     The entities of a bulk import that are written: an audit entry over a stored one is left out when it carries
    ///     the stored content and refused otherwise; every other entity is written as given.
    /// </summary>
    public static async Task<IReadOnlyList<RtEntity>> FilterImportAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService, IReadOnlyList<RtEntity> entities)
    {
        var auditEntries = entities.Where(e =>
            e.RtId != OctoObjectId.Empty && IsAuditEntry(new RtEntityId(e.GetRtCkTypeId(), e.RtId))).ToList();
        if (auditEntries.Count == 0)
        {
            return entities;
        }

        var stored = await ReadStoredAsync(session, repositoryDataSource, ckCacheService, auditEntries)
            .ConfigureAwait(false);
        var result = new OperationResult();
        var written = new List<RtEntity>(entities.Count);
        foreach (var entity in entities)
        {
            if (!auditEntries.Contains(entity) || !stored.TryGetValue(entity.RtId, out var storedEntry))
            {
                written.Add(entity);
                continue;
            }

            var unchanged = storedEntry.Attributes.Keys.Union(entity.Attributes.Keys, StringComparer.Ordinal)
                .All(name => RtAttributeValueEquality.AreEqual(entity.GetAttributeValueOrDefault(name),
                    storedEntry.GetAttributeValueOrDefault(name)));
            if (!unchanged)
            {
                Refuse(result, new RtEntityId(entity.GetRtCkTypeId(), entity.RtId), ReleasedDocumentLock.OperationImport);
            }
        }

        ThrowIfAny(result);
        return written;
    }

    private static bool IsAuditEntry(RtEntityId id) =>
        id.CkTypeId != null && string.Equals(id.CkTypeId.FullName,
            ReleasedDocumentDeniedAudit.DocumentAuditEntryCkTypeId.FullName, StringComparison.Ordinal);

    private static async Task<Dictionary<OctoObjectId, RtEntity>> ReadStoredAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService, IReadOnlyList<RtEntity> entries)
    {
        if (!ckCacheService.TryGetRtCkType(repositoryDataSource.TenantId,
                ReleasedDocumentDeniedAudit.DocumentAuditEntryCkTypeId, out var graph))
        {
            return [];
        }

        var ids = entries.Select(e => e.RtId).Distinct().ToList();
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
