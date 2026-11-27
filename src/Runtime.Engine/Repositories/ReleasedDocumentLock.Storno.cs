using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

internal static partial class ReleasedDocumentLock
{
    /// <summary>Version-less id of the association role <c>System/Cancels</c> (storno to original, design note 1.6).</summary>
    internal static readonly RtCkId<CkAssociationRoleId> CancelsRoleId = new("System/Cancels");

    /// <summary>
    ///     The storno rules (design note D6, D7 and 1.6) for one batch, before anything of it is written. A storno is a
    ///     document with an outbound <c>System/Cancels</c> association to the original it cancels; the original is not
    ///     written (an inbound association of a released document is allowed by the lock), so it stays unchanged. When
    ///     the batch creates a <c>Cancels</c> association, the original must be a released document (D7, else 169), must
    ///     not be a storno itself (171), and the storno must be of the type its number range configures
    ///     (<c>Release.StornoCkTypeId</c>, else 172). After the batch an original has at most one released storno (D6,
    ///     170): checked when the batch creates a <c>Cancels</c> association or writes a document that has one (the write
    ///     that releases a storno). Reads only when the batch creates a <c>Cancels</c> association or writes a document of
    ///     a numbered type.
    /// </summary>
    /// <returns>
    ///     The stornos the batch releases (not released before, released after the batch), each with its original, for
    ///     the <c>Storno</c> entries of the audit trail.
    /// </returns>
    public static async Task<IReadOnlyList<StornoRelease>> CheckStornosAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService, EntityRuleEngineResult<RtEntity> entities,
        IReadOnlyList<RtAssociation> createdAssociations, IReadOnlyList<RtAssociation> deletedAssociations)
    {
        var created = createdAssociations.Where(IsCancels).ToList();
        var deleted = deletedAssociations.Where(IsCancels).Select(a => a.AssociationId).ToHashSet();
        var scope = new LockScope(session, repositoryDataSource, ckCacheService);

        // The documents of numbered types the batch writes, as the write leaves them: a storno may be released here.
        var resulting = new Dictionary<OctoObjectId, (RtCkId<CkTypeId> CkTypeId, Func<RtEntity?, RtEntity> Of)>();
        foreach (var (id, partial) in entities.RtEntitiesToUpdate)
        {
            resulting[id.RtId] = (id.CkTypeId, stored => stored == null ? partial
                : NumberRangeReleaseLock.Overlay(stored, partial));
        }

        foreach (var (id, replacement) in entities.RtEntitiesToReplace)
        {
            resulting[id.RtId] = (id.CkTypeId, _ => replacement);
        }

        foreach (var inserted in entities.RtEntitiesToInsert.Where(e => e.RtId != OctoObjectId.Empty))
        {
            resulting[inserted.RtId] = (inserted.GetRtCkTypeId(), _ => inserted);
        }

        foreach (var rtId in resulting.Where(r => !scope.IsNumberedType(r.Value.CkTypeId)).Select(r => r.Key).ToList())
        {
            resulting.Remove(rtId);
        }

        if (created.Count == 0 && resulting.Count == 0)
        {
            return [];
        }

        var edges = new List<RtAssociation>(created);
        if (resulting.Count > 0)
        {
            var writtenIds = resulting.Keys.ToList();
            edges.AddRange((await repositoryDataSource.RtAssociations
                    .FindManyAsync(session, a => writtenIds.Contains(a.OriginRtId)).ConfigureAwait(false))
                .Where(a => IsCancels(a) && !deleted.Contains(a.AssociationId)));
        }

        if (edges.Count == 0)
        {
            return [];
        }

        var originalIds = edges.Select(e => e.TargetRtId).Distinct().ToList();
        var stored = (await repositoryDataSource.RtAssociations
                .FindManyAsync(session, a => originalIds.Contains(a.TargetRtId) || originalIds.Contains(a.OriginRtId))
                .ConfigureAwait(false))
            .Where(a => IsCancels(a) && !deleted.Contains(a.AssociationId)).ToList();
        var allEdges = stored.Concat(created).ToList();

        var documents = allEdges.Select(e => new RtEntityId(e.TargetCkTypeId, e.TargetRtId))
            .Concat(allEdges.Select(e => new RtEntityId(e.OriginCkTypeId, e.OriginRtId))).ToList();
        await scope.LoadAsync(documents).ConfigureAwait(false);
        await scope.EnsureRangesAsync(documents.Select(d => d.CkTypeId).DistinctBy(t => t.FullName))
            .ConfigureAwait(false);

        bool IsReleased(OctoObjectId rtId, RtCkId<CkTypeId> ckTypeId)
        {
            var storedDocument = scope.Stored(rtId);
            var type = storedDocument?.CkTypeId ?? ckTypeId;
            if (resulting.TryGetValue(rtId, out var write))
            {
                return scope.IsReleasedState(write.Of(storedDocument), type);
            }

            return storedDocument != null && scope.IsReleasedState(storedDocument, type);
        }

        var result = new OperationResult();
        foreach (var originalId in originalIds)
        {
            var original = edges.First(e => e.TargetRtId == originalId);
            var originalType = original.TargetCkTypeId.FullName;
            foreach (var edge in created.Where(e => e.TargetRtId == originalId))
            {
                var stornoType = edge.OriginCkTypeId.FullName;
                if (!scope.TryGetReleased(originalId, out var releasedOriginal))
                {
                    result.AddMessage(MessageCodes.RtStornoOfUnreleasedDocument($"{stornoType}@{edge.OriginRtId}",
                        edge.OriginRtId, stornoType, originalId, originalType));
                }
                else if (allEdges.Any(e => e.OriginRtId == originalId))
                {
                    result.AddMessage(MessageCodes.RtStornoOfStorno($"{stornoType}@{edge.OriginRtId}", originalId,
                        originalType, edge.OriginRtId));
                }
                else if (!releasedOriginal.StornoCkTypeIds.Any(t => scope.IsTypeOrDerived(edge.OriginCkTypeId, t)))
                {
                    var configured = releasedOriginal.StornoCkTypeIds.Count == 0
                        ? "none"
                        : string.Join(", ", releasedOriginal.StornoCkTypeIds);
                    result.AddMessage(MessageCodes.RtStornoTypeNotConfigured($"{stornoType}@{edge.OriginRtId}",
                        edge.OriginRtId, stornoType, originalId, originalType, configured));
                }
            }

            // D6: one released storno per original after the batch.
            var releasedStornos = allEdges.Where(e => e.TargetRtId == originalId)
                .Select(e => (e.OriginRtId, e.OriginCkTypeId)).DistinctBy(s => s.OriginRtId)
                .Where(s => IsReleased(s.OriginRtId, s.OriginCkTypeId)).Select(s => s.OriginRtId).ToList();
            if (releasedStornos.Count > 1)
            {
                // The one released before the batch is the existing storno; the others would be second ones.
                var existing = releasedStornos.FirstOrDefault(id =>
                    !resulting.ContainsKey(id) && created.All(c => c.OriginRtId != id), releasedStornos[0]);
                foreach (var second in releasedStornos.Where(id => id != existing))
                {
                    result.AddMessage(MessageCodes.RtStornoOriginalAlreadyCancelled($"{originalType}@{originalId}",
                        originalId, originalType, existing, second));
                }
            }
        }

        if (result.HasErrors || result.HasFatalErrors)
        {
            throw new StornoRefusedException(result);
        }

        // The stornos this batch releases: written by it, released after it and not released before.
        return allEdges.Where(e => resulting.ContainsKey(e.OriginRtId))
            .DistinctBy(e => (e.OriginRtId, e.TargetRtId))
            .Where(e => IsReleased(e.OriginRtId, e.OriginCkTypeId))
            .Where(e =>
            {
                var storedStorno = scope.Stored(e.OriginRtId);
                return storedStorno == null ||
                       !scope.IsReleasedState(storedStorno, storedStorno.CkTypeId ?? e.OriginCkTypeId);
            })
            .Select(e => new StornoRelease(new RtEntityId(e.OriginCkTypeId, e.OriginRtId),
                new RtEntityId(e.TargetCkTypeId, e.TargetRtId)))
            .ToList();
    }

    /// <summary>A storno a batch releases and the original it cancels.</summary>
    internal sealed record StornoRelease(RtEntityId Storno, RtEntityId Original);

    private static bool IsCancels(RtAssociation association) =>
        string.Equals(association.AssociationRoleId?.FullName, CancelsRoleId.FullName, StringComparison.Ordinal);
}
