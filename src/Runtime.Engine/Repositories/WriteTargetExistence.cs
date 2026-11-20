using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Security;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Existence step of the write funnel, before rule validation: an update, replace or delete target or association
///     origin that is not stored as the addressed type (or one of its derived types) answers "not found" exactly like
///     a record the caller may not see. Entities the guard already read are not read again; the others cost one read
///     per type. Unknown types are left to the rule validation.
/// </summary>
internal static class WriteTargetExistence
{
    internal static async Task<DataPermissionWriteGuard.NotVisibleChange?> FindFirstMissingAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IReadOnlyDictionary<RtEntityId, RtEntity> storedEntitiesReadByGuard)
    {
        var targets = DataPermissionWriteGuard.CollectStoredTargets(entityUpdateInfoList, associationUpdateInfoList);
        foreach (var targetsOfType in targets.GroupBy(target => target.CkTypeId))
        {
            if (!ckCacheService.TryGetRtCkType(repositoryDataSource.TenantId, targetsOfType.Key, out var ckTypeGraph))
            {
                continue;
            }

            var storedById = await ReadStoredEntitiesAsync(session, repositoryDataSource, ckTypeGraph,
                targetsOfType.Key, targetsOfType.ToList(), storedEntitiesReadByGuard).ConfigureAwait(false);
            var acceptedTypeNames = AcceptedStoredTypeNames(ckTypeGraph);
            var missingTarget = targetsOfType.FirstOrDefault(target =>
                !storedById.TryGetValue(target.RtId, out var storedEntity) ||
                !IsStoredAsAcceptedType(storedEntity, acceptedTypeNames));
            if (missingTarget != null)
            {
                return missingTarget;
            }
        }

        return null;
    }

    private static async Task<Dictionary<OctoObjectId, RtEntity>> ReadStoredEntitiesAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, CkTypeGraph ckTypeGraph, RtCkId<CkTypeId> addressedTypeId,
        IReadOnlyList<DataPermissionWriteGuard.NotVisibleChange> targetsOfType,
        IReadOnlyDictionary<RtEntityId, RtEntity> storedEntitiesReadByGuard)
    {
        var storedById = new Dictionary<OctoObjectId, RtEntity>();
        var unreadRtIds = new List<OctoObjectId>();
        foreach (var rtId in targetsOfType.Select(target => target.RtId).Distinct())
        {
            if (storedEntitiesReadByGuard.TryGetValue(new RtEntityId(addressedTypeId, rtId), out var storedEntity))
            {
                storedById[rtId] = storedEntity;
            }
            else
            {
                unreadRtIds.Add(rtId);
            }
        }

        if (unreadRtIds.Count == 0)
        {
            return storedById;
        }

        var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
        var readEntities = await collection.DocumentsAsync(session, unreadRtIds).ConfigureAwait(false);
        foreach (var readEntity in readEntities)
        {
            storedById.TryAdd(readEntity.RtId, readEntity);
        }

        return storedById;
    }

    private static HashSet<string> AcceptedStoredTypeNames(CkTypeGraph ckTypeGraph) =>
        new(ckTypeGraph.GetAllDerivedTypes(true).Select(typeId => typeId.ToRtCkId().SemanticVersionedFullName),
            StringComparer.Ordinal);

    private static bool IsStoredAsAcceptedType(RtEntity storedEntity, HashSet<string> acceptedTypeNames) =>
        storedEntity.CkTypeId == null || acceptedTypeNames.Contains(storedEntity.CkTypeId.SemanticVersionedFullName);
}
