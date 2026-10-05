using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Implementation of <see cref="IBulkRtMutation" />
/// </summary>
internal class BulkRtMutation(
    IEntityRuleEngine entityRuleEngine,
    IGraphRuleEngine graphRuleEngine,
    IEnumerable<IPreDocumentModification<RtEntity>> preDocumentModifications,
    ISecretWriteNormalizer secretWriteNormalizer)
    : IBulkRtMutation
{
    private readonly List<IPreDocumentModification<RtEntity>> _preDocumentModifications =
        preDocumentModifications.ToList();

    /// <inheritdoc />
    public ISecretWriteNormalizer SecretWriteNormalizer => secretWriteNormalizer;

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
                entityRuleEngineResult.RtEntitiesToUpdate, entityRuleEngineResult.UpdateGuards)
                .ConfigureAwait(false);
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

        // User sessions always stamp the caller (a client-supplied value must never win); system
        // sessions keep a provided value so import/restore round-trips the original creator.
        var securityContext = session.GetSecurityContext();
        if (!securityContext.IsSystem)
        {
            rtEntities.ForEach(x => x.RtCreatedBy = securityContext.SubjectId);
        }

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

            // AB#5532: Secret write rules before anything is written (see ApplySecretWriteRules).
            ApplySecretWriteRules(repositoryDataSource.TenantId, ckCacheService, ckTypeGraph, rtEntityGrouping.ToList(),
                SecretWriteOperation.Insert, null);

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
        IReadOnlyDictionary<RtEntityId, RtEntity> rtEntities,
        IReadOnlyDictionary<RtEntityId, AttributeNewerThanGuard> updateGuards)
    {
        foreach (var rtEntityGrouping in rtEntities.GroupBy(x => x.Key.CkTypeId))
        {
            if (string.IsNullOrWhiteSpace(rtEntityGrouping.Key.FullName))
            {
                throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(RtEntity));
            }

            await UpdateRtEntitiesByCkId(session, repositoryDataSource, ckCacheService,
                rtEntityGrouping.Key, rtEntityGrouping, updateGuards).ConfigureAwait(false);
        }
    }

    private async Task UpdateRtEntitiesByCkId(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService,
        RtCkId<CkTypeId> ckTypeId, IGrouping<RtCkId<CkTypeId>, KeyValuePair<RtEntityId, RtEntity>> rtEntityGrouping,
        IReadOnlyDictionary<RtEntityId, AttributeNewerThanGuard> updateGuards)
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

        // AB#5532: Secret write rules. An update only needs the stored entities when it rewrites a
        // record attribute containing secrets (carry-over by record key).
        if (secretWriteNormalizer.HasSecretAttributes(ckCacheService, repositoryDataSource.TenantId, ckTypeGraph))
        {
            var needStored = rtEntities.Where(e => secretWriteNormalizer.NeedsStoredEntity(ckCacheService,
                repositoryDataSource.TenantId, ckTypeGraph, e, SecretWriteOperation.Update)).ToList();
            var storedById = needStored.Count == 0
                ? new Dictionary<OctoObjectId, RtEntity>()
                : await ReadStoredEntitiesAsync(session, collection, needStored).ConfigureAwait(false);
            ApplySecretWriteRules(repositoryDataSource.TenantId, ckCacheService, ckTypeGraph, rtEntities,
                SecretWriteOperation.Update, storedById);
        }

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

        await RecomputeDisplayFieldsForUpdatesAsync(session, collection, ckTypeGraph, rtEntities)
            .ConfigureAwait(false);

        // Guarded updates are applied one-by-one with the optimistic-concurrency filter; the
        // write may be a no-op if the guard does not match (stale-write protection — see
        // AttributeNewerThanGuard). Unguarded entities still go through the batch path.
        var unguardedEntities = new List<RtEntity>();
        foreach (var keyValuePair in rtEntityGrouping)
        {
            if (updateGuards.TryGetValue(keyValuePair.Key, out var guard))
            {
                await collection.UpdateOneIfGuardMatchesAsync(session, keyValuePair.Value, guard)
                    .ConfigureAwait(false);
            }
            else
            {
                unguardedEntities.Add(keyValuePair.Value);
            }
        }

        if (unguardedEntities.Count > 0)
        {
            await collection.UpdateOneAsync(session, unguardedEntities).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Smart recompute of the display fields on partial updates (AB#4811): when an update
    ///     touches an attribute referenced by the type's effective display rules, the stored
    ///     entities are re-read once per batch and the rules are re-evaluated against stored +
    ///     updated attributes (see <see cref="DisplayFieldUpdateRecompute" />). Updates not
    ///     touching referenced attributes cause no extra read.
    /// </summary>
    private static async Task RecomputeDisplayFieldsForUpdatesAsync(IOctoSession session,
        IDataSourceCollection<OctoObjectId, RtEntity> collection, CkTypeGraph ckTypeGraph,
        IReadOnlyList<RtEntity> rtEntities)
    {
        var nameParseResult = DisplayFieldUpdateRecompute.GetValidParseResult(ckTypeGraph.DisplayNameRule);
        var descriptionParseResult =
            DisplayFieldUpdateRecompute.GetValidParseResult(ckTypeGraph.DisplayDescriptionRule);
        if (nameParseResult == null && descriptionParseResult == null)
        {
            return;
        }

        var referencedRootAttributes =
            DisplayFieldUpdateRecompute.GetReferencedRootAttributes(nameParseResult, descriptionParseResult);
        var affectedEntities = rtEntities
            .Where(e => DisplayFieldUpdateRecompute.TouchesReferencedAttributes(e, referencedRootAttributes))
            .ToList();
        if (affectedEntities.Count == 0)
        {
            return;
        }

        var rtIds = affectedEntities.Select(e => e.RtId).ToList();
        var storedEntities = await collection.FindManyAsync(session, f => rtIds.Contains(f.RtId))
            .ConfigureAwait(false);
        var storedEntitiesById = storedEntities.ToDictionary(e => e.RtId);

        foreach (var partialEntity in affectedEntities)
        {
            if (!storedEntitiesById.TryGetValue(partialEntity.RtId, out var storedEntity))
            {
                // Missing entity surfaces as MatchedCount=0 in the update itself
                continue;
            }

            DisplayFieldUpdateRecompute.Recompute(nameParseResult, descriptionParseResult, partialEntity,
                storedEntity);
        }
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
        }

        var rtEntities = rtEntityGrouping.Select(x => x.Value).ToList();

        // One read of the stored documents serves both the creator preservation and the Secret
        // carry-over (AB#5532). The Secret rules run BEFORE anything is deleted or written: a replace
        // that would leave a required secret without a value is rejected without side effects.
        var storedById = await ReadStoredEntitiesAsync(session, collection, rtEntities).ConfigureAwait(false);
        ApplySecretWriteRules(repositoryDataSource.TenantId, ckCacheService, ckTypeGraph, rtEntities,
            SecretWriteOperation.Replace, storedById);

        foreach (var keyValuePair in rtEntityGrouping)
        {
            // We need to delete the binary data from the file system if it is a linked binary
            await HandleDeleteLinkedBinary(session, repositoryDataSource, ckTypeGraph, keyValuePair.Key)
                .ConfigureAwait(false);
        }

        PreserveCreatedByForReplaces(session, storedById, rtEntities);

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

    /// <summary>
    ///     A replace rewrites the whole document, so an incoming entity without <see cref="RtEntity.RtCreatedBy" />
    ///     would erase the stored creator. The stored value always wins; a replace that creates a new document
    ///     stamps like an insert.
    /// </summary>
    private static void PreserveCreatedByForReplaces(IOctoSession session,
        IReadOnlyDictionary<OctoObjectId, RtEntity> storedById, IReadOnlyList<RtEntity> rtEntities)
    {
        var securityContext = session.GetSecurityContext();
        foreach (var rtEntity in rtEntities)
        {
            if (storedById.TryGetValue(rtEntity.RtId, out var stored))
            {
                rtEntity.RtCreatedBy = stored.RtCreatedBy;
            }
            else if (!securityContext.IsSystem)
            {
                rtEntity.RtCreatedBy = securityContext.SubjectId;
            }
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

    #region Secret attributes

    /// <summary>
    ///     Reads the stored documents of the given entities once (by rtId).
    /// </summary>
    private static async Task<Dictionary<OctoObjectId, RtEntity>> ReadStoredEntitiesAsync(IOctoSession session,
        IDataSourceCollection<OctoObjectId, RtEntity> collection, IReadOnlyList<RtEntity> rtEntities)
    {
        var rtIds = rtEntities.Select(e => e.RtId).ToList();
        var storedEntities = await collection.FindManyAsync(session, f => rtIds.Contains(f.RtId))
            .ConfigureAwait(false);
        var storedById = new Dictionary<OctoObjectId, RtEntity>();
        foreach (var stored in storedEntities)
        {
            storedById.TryAdd(stored.RtId, stored);
        }

        return storedById;
    }

    /// <summary>
    ///     AB#5532 (concept §3.6, §4.6): runs every Secret attribute - and every Secret sub-attribute of a
    ///     (nested) record - through the Secret write step, unconditionally, like the linked-binary
    ///     handling. After it no plaintext is left in a Secret slot. A required secret that ends up
    ///     without a value (e.g. a replace with nothing to carry over) rejects the whole batch; the
    ///     message names the attribute, never the value.
    /// </summary>
    private void ApplySecretWriteRules(string tenantId, ICkCacheService ckCacheService, CkTypeGraph ckTypeGraph,
        IReadOnlyList<RtEntity> rtEntities, SecretWriteOperation operation,
        IReadOnlyDictionary<OctoObjectId, RtEntity>? storedById)
    {
        if (!secretWriteNormalizer.HasSecretAttributes(ckCacheService, tenantId, ckTypeGraph))
        {
            return;
        }

        var operationResult = new OperationResult();
        foreach (var rtEntity in rtEntities)
        {
            RtEntity? stored = null;
            storedById?.TryGetValue(rtEntity.RtId, out stored);
            var result = secretWriteNormalizer.Normalize(ckCacheService, tenantId, ckTypeGraph, rtEntity, operation,
                stored);
            foreach (var attributePath in result.MissingRequiredAttributes)
            {
                operationResult.AddMessage(MessageCodes.MandatorySecretMissing(null, tenantId, attributePath,
                    rtEntity.CkTypeId ?? ckTypeGraph.CkTypeId.ToRtCkId(), rtEntity.RtId));
            }
        }

        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);
    }

    #endregion Secret attributes

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