using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Implementation of <see cref="IBulkRtMutation" />
/// </summary>
internal class BulkRtMutation(
    IEntityRuleEngine entityRuleEngine,
    IGraphRuleEngine graphRuleEngine,
    IEnumerable<IPreDocumentModification<RtEntity>> preDocumentModifications)
    : IBulkRtMutation
{
    private readonly List<IPreDocumentModification<RtEntity>> _preDocumentModifications =
        preDocumentModifications.ToList();

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, BulkRtMutationOptions options)
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
        var entityValidatorResult = await entityRuleEngine.ValidateAsync(repositoryDataSource.TenantId,
            entityUpdateInfoList, originFileResolver, operationResult).ConfigureAwait(false);

        var graphValidationResult =
            await graphRuleEngine
                .ValidateAsync(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList,
                    originFileResolver,
                    operationResult)
                .ConfigureAwait(false);

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);

        await ApplyRtEntityChangesAsync(session, repositoryDataSource, ckCacheService, entityValidatorResult, options)
            .ConfigureAwait(false);
        await ApplyRtAssociationChangesAsync(session, repositoryDataSource, graphValidationResult)
            .ConfigureAwait(false);
    }

    private async Task ApplyRtEntityChangesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        EntityRuleEngineResult<RtEntity> entityRuleEngineResult, BulkRtMutationOptions options)
    {
        if (entityRuleEngineResult.RtEntitiesToDelete.Any())
        {
            await DeleteRtEntityAsync(session, repositoryDataSource, ckCacheService,
                entityRuleEngineResult.RtEntitiesToDelete, options).ConfigureAwait(false);
        }

        if (entityRuleEngineResult.RtEntitiesToUpdate.Any())
        {
            await UpdateRtEntities(session, repositoryDataSource, ckCacheService,
                entityRuleEngineResult.RtEntitiesToUpdate).ConfigureAwait(false);
        }

        if (entityRuleEngineResult.RtEntitiesToReplace.Any())
        {
            await ReplaceRtEntities(session, repositoryDataSource, ckCacheService,
                entityRuleEngineResult.RtEntitiesToReplace, options).ConfigureAwait(false);
        }

        if (entityRuleEngineResult.RtEntitiesToInsert.Any())
        {
            await InsertRtEntitiesAsync(session, repositoryDataSource, ckCacheService,
                entityRuleEngineResult.RtEntitiesToInsert, options).ConfigureAwait(false);
        }
    }

    private async Task InsertRtEntitiesAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        IEnumerable<RtEntity> rtEntityList,
        BulkRtMutationOptions options)
    {
        var rtEntities = rtEntityList.ToList();
        rtEntities.ForEach(x => x.RtCreationDateTime = DateTime.Now);
        rtEntities.ForEach(x => x.RtChangedDateTime = x.RtCreationDateTime);
        rtEntities.ForEach(x => { x.CkTypeId ??= x.GetRtCkTypeId(); });

        if (!options.DisablePreDocumentModifications)
        {
            foreach (var preDocumentModification in _preDocumentModifications)
            {
                await preDocumentModification.RunAsync(session, repositoryDataSource, rtEntities)
                    .ConfigureAwait(false);
            }
        }

        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.GetRtCkTypeId()))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            var ckTypeId = rtEntityGrouping.Key;

            var ckTypeGraph = ckCacheService.GetRtCkType(repositoryDataSource.TenantId, ckTypeId);
            await HandleUploadLinkedBinary(session, repositoryDataSource, ckTypeGraph, rtEntityGrouping.ToList())
                .ConfigureAwait(false);

            var rtCollection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
            if (options.UseBulkMode)
            {
                await rtCollection.BulkImportAsync(session, rtEntityGrouping,
                    new BulkOperationOptions { InsertStrategy = options.BulkInsertStrategy }).ConfigureAwait(false);
            }
            else
            {
                await rtCollection.InsertManyAsync(session, rtEntityGrouping).ConfigureAwait(false);
            }
        }
    }

    private async Task ReplaceRtEntities(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        IReadOnlyDictionary<RtEntityId, RtEntity> rtEntities, BulkRtMutationOptions options)
    {
        if (!options.DisablePreDocumentModifications)
        {
            foreach (var preDocumentModification in _preDocumentModifications)
            {
                await preDocumentModification.RunAsync(session, repositoryDataSource, rtEntities.Values)
                    .ConfigureAwait(false);
            }
        }

        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.Key.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await ReplaceRtEntitiesByCkId(session, repositoryDataSource, ckCacheService,
                rtEntityGrouping.Key, rtEntityGrouping, options).ConfigureAwait(false);
        }
    }

    private async Task UpdateRtEntities(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
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
        RtCkId<CkTypeId> ckTypeId, IGrouping<RtCkId<CkTypeId>, KeyValuePair<RtEntityId, RtEntity>> rtEntityGrouping)
    {
        var ckTypeGraph = ckCacheService.GetRtCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);

        foreach (var keyValuePair in rtEntityGrouping)
        {
            keyValuePair.Value.RtId = keyValuePair.Key.RtId;
            keyValuePair.Value.CkTypeId = keyValuePair.Key.CkTypeId;
            keyValuePair.Value.RtChangedDateTime = DateTime.UtcNow;
        }

        var rtEntities = rtEntityGrouping.Select(x => x.Value).ToList();

        foreach (var ckTypeAttributeGraph in ckTypeGraph.AllAttributes.Values.Where(a =>
                     a.ValueType == AttributeValueTypesDto.BinaryLinked))
        {
            foreach (var rtEntity in rtEntities)
            {
                var entityBinaryInfo =
                    rtEntity.GetAttributeLinkedBinaryValueOrDefault(ckTypeAttributeGraph.AttributeName);
                if (entityBinaryInfo != null)
                {
                    if (entityBinaryInfo.Stream == null)
                    {
                        throw RuntimeRepositoryException.StreamDataIsMissing(rtEntity.ToRtEntityId());
                    }

                    entityBinaryInfo.Size = entityBinaryInfo.Stream.Length;

                    if (entityBinaryInfo.BinaryId == null)
                    {
                        var binaryId = await repositoryDataSource.BinaryDataSource
                            .UploadFileSystemBinaryAsync(session, rtEntity.ToRtEntityId(), entityBinaryInfo.Filename,
                                entityBinaryInfo.ContentType, entityBinaryInfo.Stream, CancellationToken.None)
                            .ConfigureAwait(false);
                        entityBinaryInfo.BinaryId = binaryId;
                    }
                    else
                    {
                        await repositoryDataSource.BinaryDataSource
                            .ReplaceFileSystemBinaryAsync(session, entityBinaryInfo.BinaryId.Value,
                                entityBinaryInfo.Filename, entityBinaryInfo.ContentType, entityBinaryInfo.Stream,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }
        }


        await collection.UpdateOneAsync(session, rtEntities).ConfigureAwait(false);
    }

    private async Task ReplaceRtEntitiesByCkId(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        RtCkId<CkTypeId> ckTypeId, IGrouping<RtCkId<CkTypeId>, KeyValuePair<RtEntityId, RtEntity>> rtEntityGrouping,
        BulkRtMutationOptions options)
    {
        var ckTypeGraph = ckCacheService.GetRtCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);

        foreach (var keyValuePair in rtEntityGrouping)
        {
            keyValuePair.Value.RtId = keyValuePair.Key.RtId;
            keyValuePair.Value.CkTypeId = keyValuePair.Key.CkTypeId;
            keyValuePair.Value.RtChangedDateTime = DateTime.UtcNow;

            // We need to delete the binary data from the file system if it is a linked binary
            await HandleDeleteLinkedBinary(session, repositoryDataSource, ckTypeGraph, keyValuePair.Key)
                .ConfigureAwait(false);
        }

        var rtEntities = rtEntityGrouping.Select(x => x.Value).ToList();

        // Upload the new linked binary data
        await HandleUploadLinkedBinary(session, repositoryDataSource, ckTypeGraph, rtEntities).ConfigureAwait(false);

        if (options.UseBulkMode)
        {
            // For replace operations, always use Upsert strategy to ensure existing entities are updated
            await collection.BulkImportAsync(session, rtEntities,
                new BulkOperationOptions { InsertStrategy = BulkInsertStrategies.Upsert }).ConfigureAwait(false);
        }
        else
        {
            await collection.ReplaceManyAsync(session, rtEntities).ConfigureAwait(false);
        }
    }

    private async Task DeleteRtEntityAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        IReadOnlyList<RtEntityId> rtEntityIds, BulkRtMutationOptions options)
    {
        foreach (var rtEntityGrouping in rtEntityIds.GroupBy(x => x.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await DeleteRtEntityAsync<RtEntity>(session, repositoryDataSource, ckCacheService, rtEntityGrouping.Key,
                    rtEntityGrouping, options)
                .ConfigureAwait(false);
        }
    }

    private async Task DeleteRtEntityAsync<TEntity>(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        RtCkId<CkTypeId> ckTypeId, IEnumerable<RtEntityId> rtEntityIds, BulkRtMutationOptions options)
        where TEntity : RtEntity, new()
    {
        var ckTypeGraph = ckCacheService.GetRtCkType(repositoryDataSource.TenantId, ckTypeId);
        var collection = repositoryDataSource.GetRtCollection<TEntity>(ckTypeGraph);

        if (options.DeleteStrategy == DeleteStrategies.Archive)
        {
            // This case only set the state to delete.
            List<TEntity> updatedEntities = new List<TEntity>();
            foreach (var rtEntityId in rtEntityIds)
            {
                updatedEntities.Add(new TEntity
                {
                    RtId = rtEntityId.RtId,
                    CkTypeId = ckTypeId,
                    RtChangedDateTime = DateTime.UtcNow,
                    RtArchivedDateTime = DateTime.UtcNow,
                    RtState = RtState.Archived
                });

                await repositoryDataSource.RtAssociations.UpdateManyAsync(session,
                    a => (a.OriginCkTypeId == ckTypeId && a.OriginRtId == rtEntityId.RtId) ||
                         (a.TargetCkTypeId == ckTypeId && a.TargetRtId == rtEntityId.RtId), new RtAssociation
                    {
                        RtState = RtState.Archived
                    }).ConfigureAwait(false);
            }

            await collection.UpdateOneAsync(session, updatedEntities).ConfigureAwait(false);
        }
        else
        {
            foreach (var rtEntityId in rtEntityIds.AsParallel())
            {
                // Delete the entity from the database
                await collection.DeleteOneAsync(session, rtEntityId.RtId).ConfigureAwait(false);
                // We need to delete the binary data from the file system if it is a linked binary
                await HandleDeleteLinkedBinary(session, repositoryDataSource, ckTypeGraph, rtEntityId)
                    .ConfigureAwait(false);
            }
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
        await repositoryDataSource.RtAssociations.DeleteOneAsync(session, rtAssociations.Select(x => x.AssociationId))
            .ConfigureAwait(false);
    }

    #region Linked Binary

    private static async Task HandleDeleteLinkedBinary(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, CkTypeGraph ckTypeGraph, RtEntityId rtEntityId)
    {
        if (ckTypeGraph.AllAttributes.Values.Any(x => x.ValueType == AttributeValueTypesDto.BinaryLinked))
        {
            await repositoryDataSource.BinaryDataSource.DeleteAllFileSystemBinariesAsync(session, rtEntityId,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task HandleUploadLinkedBinary(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        CkTypeGraph ckTypeGraph, IReadOnlyList<RtEntity> rtEntities)
    {
        foreach (var ckTypeAttributeGraph in ckTypeGraph.AllAttributes.Values.Where(a =>
                     a.ValueType == AttributeValueTypesDto.BinaryLinked))
        {
            foreach (var rtEntity in rtEntities)
            {
                var entityBinaryInfo =
                    rtEntity.GetAttributeLinkedBinaryValueOrDefault(ckTypeAttributeGraph.AttributeName);
                if (entityBinaryInfo != null)
                {
                    if (entityBinaryInfo.Stream == null)
                    {
                        throw RuntimeRepositoryException.StreamDataIsMissing(rtEntity.ToRtEntityId());
                    }

                    entityBinaryInfo.Size = entityBinaryInfo.Stream.Length;

                    var binaryId = await repositoryDataSource.BinaryDataSource
                        .UploadFileSystemBinaryAsync(session, rtEntity.ToRtEntityId(), entityBinaryInfo.Filename,
                            entityBinaryInfo.ContentType, entityBinaryInfo.Stream, CancellationToken.None)
                        .ConfigureAwait(false);
                    entityBinaryInfo.BinaryId = binaryId;
                }
            }
        }
    }

    #endregion Linked Binary
}