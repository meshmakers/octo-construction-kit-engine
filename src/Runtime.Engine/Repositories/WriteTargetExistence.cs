using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Security;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Existence step of the write funnel, before rule validation: a missing update, replace or delete target or
///     association origin answers "not found" exactly like a record the caller may not see. Targets the guard already
///     read are not read again; the others cost one count per type. Unknown types are left to the rule validation.
/// </summary>
internal static class WriteTargetExistence
{
    internal static async Task<DataPermissionWriteGuard.NotVisibleChange?> FindFirstMissingAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IReadOnlySet<RtEntityId> knownStoredTargets)
    {
        var unresolvedTargets = DataPermissionWriteGuard
            .CollectStoredTargets(entityUpdateInfoList, associationUpdateInfoList)
            .Where(target => !knownStoredTargets.Contains(new RtEntityId(target.CkTypeId, target.RtId)))
            .ToList();

        foreach (var targetsOfType in unresolvedTargets.GroupBy(target => target.CkTypeId))
        {
            if (!ckCacheService.TryGetRtCkType(repositoryDataSource.TenantId, targetsOfType.Key, out var ckTypeGraph))
            {
                continue;
            }

            var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
            var missingTarget = await FindFirstMissingOfTypeAsync(session, collection, targetsOfType.ToList())
                .ConfigureAwait(false);
            if (missingTarget != null)
            {
                return missingTarget;
            }
        }

        return null;
    }

    private static async Task<DataPermissionWriteGuard.NotVisibleChange?> FindFirstMissingOfTypeAsync(
        IOctoSession session, IDataSourceCollection<OctoObjectId, RtEntity> collection,
        IReadOnlyList<DataPermissionWriteGuard.NotVisibleChange> targetsOfType)
    {
        var distinctRtIds = targetsOfType.Select(target => target.RtId).Distinct().ToList();
        var storedCount = await collection
            .GetTotalCountAsync(session, entity => distinctRtIds.Contains(entity.RtId))
            .ConfigureAwait(false);
        if (storedCount >= distinctRtIds.Count)
        {
            return null;
        }

        var storedEntities = await collection.DocumentsAsync(session, distinctRtIds).ConfigureAwait(false);
        var storedRtIds = storedEntities.Select(entity => entity.RtId).ToHashSet();
        return targetsOfType.FirstOrDefault(target => !storedRtIds.Contains(target.RtId));
    }
}
