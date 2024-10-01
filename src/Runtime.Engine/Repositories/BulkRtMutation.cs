using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Implementation of <see cref="IBulkRtMutation" />
/// </summary>
internal class BulkRtMutation : IBulkRtMutation
{
    private readonly IEntityRuleEngine _entityRuleEngine;
    private readonly IGraphRuleEngine _graphRuleEngine;
    private readonly List<IPreDocumentModification<RtEntity>> _preDocumentModifications;

    public BulkRtMutation(
        IEntityRuleEngine entityRuleEngine, IGraphRuleEngine graphRuleEngine,
        IEnumerable<IPreDocumentModification<RtEntity>> preDocumentModifications)
    {
        _entityRuleEngine = entityRuleEngine;
        _graphRuleEngine = graphRuleEngine;
        _preDocumentModifications = preDocumentModifications.ToList();
    }

    /// <summary>
    ///     Applies the changes to the data source
    /// </summary>
    /// <param name="session"></param>
    /// <param name="repositoryDataSource"></param>
    /// <param name="ckCacheService"></param>
    /// <param name="entityUpdateInfoList"></param>
    /// <param name="associationUpdateInfoList"></param>
    public async Task ApplyChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        foreach (var entityUpdateInfo in entityUpdateInfoList)
        {
            if (entityUpdateInfo.RtEntity != null)
            {
                entityUpdateInfo.RtEntity.CkTypeId = entityUpdateInfo.CkTypeId;
            }
        }
        
        OperationResult operationResult = new();
        OriginFileResolver originFileResolver = new("-");
        var entityValidatorResult = await _entityRuleEngine.ValidateAsync(repositoryDataSource.TenantId,
            entityUpdateInfoList, originFileResolver, operationResult).ConfigureAwait(false);

        var graphValidationResult =
            await _graphRuleEngine
                .ValidateAsync(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList, originFileResolver,
                    operationResult)
                .ConfigureAwait(false);

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);

        await ApplyRtEntityChangesAsync(session, repositoryDataSource, ckCacheService, entityValidatorResult).ConfigureAwait(false);
        await ApplyRtAssociationChangesAsync(session, repositoryDataSource, graphValidationResult).ConfigureAwait(false);
    }

    private async Task ApplyRtEntityChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        EntityRuleEngineResult<RtEntity> ckEntityRuleEngineResult)
    {
        if (ckEntityRuleEngineResult.RtEntitiesToDelete.Any())
        {
            await DeleteRtEntityAsync(session, repositoryDataSource, ckCacheService, ckEntityRuleEngineResult.RtEntitiesToDelete).ConfigureAwait(false);
        }

        if (ckEntityRuleEngineResult.RtEntitiesToUpdate.Any())
        {
            await UpdateRtEntities(session, repositoryDataSource, ckCacheService, ckEntityRuleEngineResult.RtEntitiesToUpdate).ConfigureAwait(false);
        }

        if (ckEntityRuleEngineResult.RtEntitiesToReplace.Any())
        {
            await ReplaceRtEntities(session, repositoryDataSource, ckCacheService, ckEntityRuleEngineResult.RtEntitiesToReplace).ConfigureAwait(false);
        }

        if (ckEntityRuleEngineResult.RtEntitiesToInsert.Any())
        {
            await InsertRtEntitiesAsync(session, repositoryDataSource, ckCacheService, ckEntityRuleEngineResult.RtEntitiesToInsert).ConfigureAwait(false);
        }
    }

    private async Task InsertRtEntitiesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IEnumerable<RtEntity> rtEntityList,
        bool disablePreDocumentModifications = false)
    {
        var rtEntities = rtEntityList.ToList();
        rtEntities.ForEach(x => x.RtCreationDateTime = DateTime.Now);
        rtEntities.ForEach(x => x.RtChangedDateTime = x.RtCreationDateTime);
        rtEntities.ForEach(x => { x.CkTypeId ??= x.GetCkTypeId(); });

        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.GetCkTypeId()))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            var ckTypeId = rtEntityGrouping.Key;

            if (!disablePreDocumentModifications)
            {
                foreach (var preDocumentModification in _preDocumentModifications)
                {
                    await preDocumentModification.RunAsync(session, rtEntityGrouping).ConfigureAwait(false);
                }

                var ckTypeGraph = ckCacheService.GetCkType(repositoryDataSource.TenantId, ckTypeId);
                var rtCollection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
                await rtCollection.InsertManyAsync(session, rtEntityGrouping).ConfigureAwait(false);
            }
        }
    }

    private async Task ReplaceRtEntities(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyDictionary<RtEntityId, RtEntity> rtEntities)
    {
        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.Key.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await ReplaceRtEntitiesByCkId(session, repositoryDataSource, ckCacheService, 
                rtEntityGrouping.Key, rtEntityGrouping).ConfigureAwait(false);
        }
    }

    private async Task UpdateRtEntities(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyDictionary<RtEntityId, RtEntity> rtEntities)
    {
        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.Key.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await UpdateRtEntitiesByCkId(session, repositoryDataSource, ckCacheService,
                rtEntityGrouping.Key, rtEntityGrouping).ConfigureAwait(false);
        }
    }

    private async Task UpdateRtEntitiesByCkId(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        CkId<CkTypeId> ckTypeId, IGrouping<CkId<CkTypeId>, KeyValuePair<RtEntityId, RtEntity>> rtEntityGrouping)
    {
        var ckTypeGraph = ckCacheService.GetCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);

        foreach (var keyValuePair in rtEntityGrouping)
        {
            keyValuePair.Value.RtId = keyValuePair.Key.RtId;
            keyValuePair.Value.CkTypeId = keyValuePair.Key.CkTypeId;
            keyValuePair.Value.RtChangedDateTime = DateTime.UtcNow;
        }

        await collection.UpdateManyAsync(session, rtEntityGrouping.Select(x => x.Value)).ConfigureAwait(false);
    }

    private async Task ReplaceRtEntitiesByCkId(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        CkId<CkTypeId> ckTypeId, IGrouping<CkId<CkTypeId>, KeyValuePair<RtEntityId, RtEntity>> rtEntityGrouping)
    {
        var ckTypeGraph = ckCacheService.GetCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);

        foreach (var keyValuePair in rtEntityGrouping)
        {
            keyValuePair.Value.RtId = keyValuePair.Key.RtId;
            keyValuePair.Value.CkTypeId = keyValuePair.Key.CkTypeId;
            keyValuePair.Value.RtChangedDateTime = DateTime.UtcNow;
        }

        await collection.ReplaceManyAsync(session, rtEntityGrouping.Select(x => x.Value)).ConfigureAwait(false);
    }

    private async Task DeleteRtEntityAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<RtEntityId> rtEntityIds)
    {
        foreach (var rtEntityGrouping in rtEntityIds.GroupBy(x => x.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await DeleteRtEntityAsync<RtEntity>(session, repositoryDataSource, ckCacheService, rtEntityGrouping.Key, rtEntityGrouping)
                .ConfigureAwait(false);
        }
    }

    private async Task DeleteRtEntityAsync<TEntity>(IOctoSession session, IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        CkId<CkTypeId> ckTypeId, IEnumerable<RtEntityId> rtEntityIds)
        where TEntity : RtEntity, new()
    {
        var ckTypeGraph = ckCacheService.GetCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<TEntity>(ckTypeGraph);

        foreach (var rtEntityId in rtEntityIds.AsParallel())
        {
            await collection.DeleteOneAsync(session, rtEntityId.RtId).ConfigureAwait(false);
        }
    }

    private async Task ApplyRtAssociationChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        GraphRuleEngineResult graphRuleEngineResult)
    {
        if (graphRuleEngineResult.RtAssociationsToDelete.Any())
        {
            await DeleteRtAssociationsAsync(session, repositoryDataSource, graphRuleEngineResult.RtAssociationsToDelete)
                .ConfigureAwait(false);
        }

        if (graphRuleEngineResult.RtAssociationsToCreate.Any())
        {
            await InsertRtAssociationsAsync(session, repositoryDataSource, graphRuleEngineResult.RtAssociationsToCreate)
                .ConfigureAwait(false);
        }
    }

    private async Task InsertRtAssociationsAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<RtAssociation> rtAssociations)
    {
        await repositoryDataSource.RtAssociations.InsertManyAsync(session, rtAssociations).ConfigureAwait(false);
    }

    private async Task DeleteRtAssociationsAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<RtAssociation> rtAssociations)
    {
        await repositoryDataSource.RtAssociations.DeleteManyAsync(session, rtAssociations.Select(x => x.AssociationId))
            .ConfigureAwait(false);
    }
}