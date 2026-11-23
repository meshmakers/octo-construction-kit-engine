using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Security;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Stored-type step of the write funnel for callers under data-permission policies: an update, replace or delete
///     target, or an association origin, whose record is stored as a type outside the addressed subtree does not exist
///     under the addressed type. Only types whose collection also holds concrete types outside their own subtree are
///     read; the raw reads of the data-permission guard are reused, the others cost one read per such type.
/// </summary>
internal static class StoredTypeTargets
{
    internal static async Task<DataPermissionWriteGuard.NotVisibleChange?> FindFirstStoredAsAnotherTypeAsync(
        IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IReadOnlyDictionary<RtEntityId, RtEntity?> rawReadsOfTheGuard)
    {
        var tenantId = repositoryDataSource.TenantId;
        var targets = DataPermissionWriteGuard.CollectStoredTargets(entityUpdateInfoList, associationUpdateInfoList);
        foreach (var targetsOfType in targets.GroupBy(target => target.CkTypeId))
        {
            if (!ckCacheService.TryGetRtCkType(tenantId, targetsOfType.Key, out var addressedTypeGraph) ||
                !StoredTypeMatch.SharesCollectionWithOtherTypes(addressedTypeGraph, ckCacheService, tenantId))
            {
                continue;
            }

            var storedByRtId = await ReadStoredRecordsAsync(session,
                repositoryDataSource.GetRtCollection<RtEntity>(addressedTypeGraph), targetsOfType.Key,
                targetsOfType.Select(target => target.RtId).Distinct().ToList(), rawReadsOfTheGuard)
                .ConfigureAwait(false);
            var acceptedTypeNames = StoredTypeMatch.AcceptedStoredTypeNames(addressedTypeGraph);
            var targetOfAnotherType = targetsOfType.FirstOrDefault(target =>
                storedByRtId.TryGetValue(target.RtId, out var storedRecord) &&
                !StoredTypeMatch.IsStoredAsAcceptedType(storedRecord, acceptedTypeNames));
            if (targetOfAnotherType != null)
            {
                return targetOfAnotherType;
            }
        }

        return null;
    }

    private static async Task<Dictionary<OctoObjectId, RtEntity>> ReadStoredRecordsAsync(IOctoSession session,
        IDataSourceCollection<OctoObjectId, RtEntity> collection,
        RtCkId<CkTypeId> addressedTypeId, IReadOnlyList<OctoObjectId> rtIds,
        IReadOnlyDictionary<RtEntityId, RtEntity?> rawReadsOfTheGuard)
    {
        var storedByRtId = new Dictionary<OctoObjectId, RtEntity>();
        var unreadRtIds = new List<OctoObjectId>();
        foreach (var rtId in rtIds)
        {
            if (!rawReadsOfTheGuard.TryGetValue(new RtEntityId(addressedTypeId, rtId), out var rawRecord))
            {
                unreadRtIds.Add(rtId);
            }
            else if (rawRecord != null)
            {
                storedByRtId[rtId] = rawRecord;
            }
        }

        if (unreadRtIds.Count == 0)
        {
            return storedByRtId;
        }

        var readRecords = await collection.DocumentsAsync(session, unreadRtIds).ConfigureAwait(false);
        foreach (var readRecord in readRecords)
        {
            storedByRtId.TryAdd(readRecord.RtId, readRecord);
        }

        return storedByRtId;
    }
}
