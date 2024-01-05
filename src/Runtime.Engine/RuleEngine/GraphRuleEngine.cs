using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
///     Implementation of the runtime graph validation engine
/// </summary>
internal class GraphRuleEngine : IGraphRuleEngine
{
    private readonly ICkCacheService _ckCache;

    public GraphRuleEngine(ICkCacheService ckCache)
    {
        _ckCache = ckCache;
    }

    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, entityUpdateInfoList, new List<AssociationUpdateInfo>(), operationResult)
            .ConfigureAwait(false);
    }


    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        var graphValidationResult = new GraphRuleEngineResult();

        // Validate if the associations are valid to be added/deleted based on the current database content
        var createAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Create);
        var deleteAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Delete);

        await ValidateAssociationsToCreate(session, repositoryDataSource, createAssociations, graphValidationResult, operationResult)
            .ConfigureAwait(false);
        await ValidateAssociationsToDelete(session, repositoryDataSource, deleteAssociations, graphValidationResult, operationResult)
            .ConfigureAwait(false);

        // Validate the consistency of the construction kit model
        await ValidateCkModel(session, repositoryDataSource, graphValidationResult, entityUpdateInfoList, associationUpdateInfoList,
            operationResult).ConfigureAwait(false);

        return graphValidationResult;
    }

    // ReSharper disable once UnusedMember.Global
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, new List<IEntityUpdateInfo<RtEntity>>(), associationUpdateInfoList,
            operationResult).ConfigureAwait(false);
    }

    private async Task ValidateCkModel(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        GraphRuleEngineResult graphRuleEngineResult,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        await ValidateOrigin(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList, operationResult)
            .ConfigureAwait(false);
        await ValidateTarget(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList, operationResult)
            .ConfigureAwait(false);

        // Ensure that all associations exists when creating an entity
        // Currently, the only mandatory association has multiplicity of One
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Insert))
        {
            var ckTypeGraph = _ckCache.GetCkType(repositoryDataSource.TenantId, entityUpdateInfo.RtEntityId.CkTypeId);

            var inputAssociationGraphs =
                ckTypeGraph.Associations.In.All.Where(a =>
                    a.Multiplicity == MultiplicitiesDto.One);
            foreach (var inputAssociationGraph in inputAssociationGraphs)
            {
                if (!associationUpdateInfoList.Any(x =>
                        x.ModOption == AssociationModOptionsDto.Create &&
                        x.RoleId == inputAssociationGraph.CkRoleId))
                {
                    operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnCreate(repositoryDataSource.TenantId,
                        entityUpdateInfo.RtEntityId.CkTypeId, entityUpdateInfo.RtEntityId.RtId, inputAssociationGraph.CkRoleId,
                        MultiplicitiesDto.One));
                }
            }
        }

        // Delete all corresponding associations if an entity is deleted  
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Delete))
        {
            var result = await repositoryDataSource.GetRtAssociationsAsync(session,
                entityUpdateInfo.RtEntityId.RtId, GraphDirections.Any).ConfigureAwait(false);
            graphRuleEngineResult.RtAssociationsToDelete.AddRange(result);
        }
    }

    private async Task ValidateTarget(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult)
    {
        var targetList = associationUpdateInfoList.Select(a => a.Target).Distinct();
        foreach (var targetRtId in targetList)
        {
            var targetEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList, targetRtId, operationResult)
                .ConfigureAwait(false);
            if (targetEntity == null)
            {
                operationResult.AddMessage(MessageCodes.MissingTargetEntity(repositoryDataSource.TenantId, targetRtId.CkTypeId,
                    targetRtId.RtId));
                continue;
            }

            var targetCacheItem = _ckCache.GetCkType(repositoryDataSource.TenantId, targetEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Target == targetRtId)
                         .GroupBy(a => a.RoleId))
            {
                var inboundAssociationCacheItem =
                    targetCacheItem.Associations.In.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (inboundAssociationCacheItem == null)
                {
                    operationResult.AddMessage(MessageCodes.AssociationNotAllowed(repositoryDataSource.TenantId, targetRtId.CkTypeId,
                        targetRtId.RtId, associationUpdateInfosByRoleId.Key));
                    continue;
                }

                var originCkTypeGraph = _ckCache.GetCkType(repositoryDataSource.TenantId, inboundAssociationCacheItem.OriginCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var originEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList,
                        associationUpdateInfo.Origin, operationResult).ConfigureAwait(false);
                    if (originEntity != null)
                    {
                        var originCacheItem = _ckCache.GetCkType(repositoryDataSource.TenantId, originEntity.GetCkTypeId());

                        if (originCkTypeGraph.CkTypeId != originCacheItem.CkTypeId &&
                            originCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != originCacheItem.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.AssociationNotAllowed(repositoryDataSource.TenantId,
                                targetRtId.CkTypeId, targetRtId.RtId, associationUpdateInfosByRoleId.Key));
                        }
                    }
                }

                var storedTargetAssociations = await repositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(session,
                    targetRtId, associationUpdateInfosByRoleId.Key, GraphDirections.Inbound).ConfigureAwait(false);

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    if (storedTargetAssociations == CurrentMultiplicity.One &&
                        inboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(repositoryDataSource.TenantId,
                            targetRtId.CkTypeId, targetRtId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedTargetAssociations == CurrentMultiplicity.One &&
                        (inboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One ||
                         inboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(repositoryDataSource.TenantId,
                            targetRtId.CkTypeId, targetRtId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }
            }
        }
    }

    private async Task ValidateOrigin(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult)
    {
        var originList = associationUpdateInfoList.Select(a => a.Origin).Distinct();
        foreach (var originRtId in originList)
        {
            var originEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList, originRtId, operationResult)
                .ConfigureAwait(false);
            if (originEntity == null)
            {
                operationResult.AddMessage(MessageCodes.MissingOriginEntity(repositoryDataSource.TenantId,
                    originRtId.CkTypeId, originRtId.RtId));
                continue;
            }

            var originCacheItem = _ckCache.GetCkType(repositoryDataSource.TenantId, originEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Origin == originRtId)
                         .GroupBy(a => a.RoleId))
            {
                var outboundAssociationCacheItem =
                    originCacheItem.Associations.Out.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (outboundAssociationCacheItem == null)
                {
                    operationResult.AddMessage(MessageCodes.InboundAssociationNotAllowedForCkType(repositoryDataSource.TenantId,
                        originRtId.CkTypeId, originRtId.RtId, associationUpdateInfosByRoleId.Key, originCacheItem.CkTypeId));
                    continue;
                }

                var targetCkTypeGraph = _ckCache.GetCkType(repositoryDataSource.TenantId, outboundAssociationCacheItem.TargetCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var targetEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList,
                        associationUpdateInfo.Target, operationResult).ConfigureAwait(false);
                    if (targetEntity != null)
                    {
                        var targetCacheItem = _ckCache.GetCkType(repositoryDataSource.TenantId, targetEntity.GetCkTypeId());

                        if (targetCkTypeGraph.CkTypeId != targetCacheItem.CkTypeId &&
                            targetCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != targetCacheItem.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.OutboundAssociationNotAllowedForCkType(repositoryDataSource.TenantId,
                                originRtId.CkTypeId, originRtId.RtId, associationUpdateInfosByRoleId.Key, targetCacheItem.CkTypeId));
                        }
                    }
                }

                var storedOriginAssociations = await repositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(session,
                    originRtId, associationUpdateInfosByRoleId.Key, GraphDirections.Outbound).ConfigureAwait(false);

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    if (storedOriginAssociations == CurrentMultiplicity.One &&
                        outboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(repositoryDataSource.TenantId,
                            originRtId.CkTypeId, originRtId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedOriginAssociations == CurrentMultiplicity.One &&
                        (outboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One ||
                         outboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(repositoryDataSource.TenantId,
                            originRtId.CkTypeId, originRtId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }
            }
        }
    }

    private async Task ValidateAssociationsToDelete(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<AssociationUpdateInfo> deleteAssociations, GraphRuleEngineResult graphRuleEngineResult, OperationResult operationResult)
    {
        foreach (var d in deleteAssociations)
        {
            var origin = d.Origin;
            var target = d.Target;

            var rtAssociation = await repositoryDataSource.GetRtAssociationOrDefaultAsync(session,
                origin,
                target,
                d.RoleId).ConfigureAwait(false);
            if (rtAssociation == null)
            {
                operationResult.AddMessage(MessageCodes.AssociationDoesNotExist(repositoryDataSource.TenantId, d.RoleId,
                    origin.CkTypeId, origin.RtId, target.CkTypeId, target.RtId));
                continue;
            }

            graphRuleEngineResult.RtAssociationsToDelete.Add(rtAssociation);
        }
    }

    private async Task ValidateAssociationsToCreate(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<AssociationUpdateInfo> createAssociations, GraphRuleEngineResult graphRuleEngineResult, OperationResult operationResult)
    {
        foreach (var associationUpdateInfo in createAssociations)
        {
            var origin = associationUpdateInfo.Origin;
            var target = associationUpdateInfo.Target;

            var rtAssociation = await repositoryDataSource.GetRtAssociationOrDefaultAsync(session,
                origin,
                target,
                associationUpdateInfo.RoleId).ConfigureAwait(false);
            if (rtAssociation != null)
            {
                operationResult.AddMessage(MessageCodes.AssociationAlreadyExists(repositoryDataSource.TenantId,
                    associationUpdateInfo.RoleId,
                    origin.CkTypeId, origin.RtId, target.CkTypeId, target.RtId));
                continue;
            }

            graphRuleEngineResult.RtAssociationsToCreate.Add(repositoryDataSource.CreateTransientRtAssociation(
                new RtEntityId(associationUpdateInfo.Origin.CkTypeId, associationUpdateInfo.Origin.RtId),
                associationUpdateInfo.RoleId,
                new RtEntityId(associationUpdateInfo.Target.CkTypeId, associationUpdateInfo.Target.RtId)));
        }
    }


    private async Task<RtEntity?> GetEntityAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        RtEntityId rtEntityId, OperationResult operationResult)
    {
        var rtEntity = entityUpdateInfoList.Select(x => x.RtEntity)
            .FirstOrDefault(x => x?.RtId == rtEntityId.RtId);
        // ReSharper disable once ConvertIfStatementToNullCoalescingExpression
        if (rtEntity == null)
        {
            var collection = repositoryDataSource.GetRtCollection<RtEntity>(rtEntityId.CkTypeId);
            rtEntity = await collection.DocumentAsync(session, rtEntityId.RtId).ConfigureAwait(false);
        }

        if (rtEntity == null)
        {
            operationResult.AddMessage(MessageCodes.EntityNotFound(repositoryDataSource.TenantId,
                rtEntityId.CkTypeId, rtEntityId.RtId));
        }

        return rtEntity;
    }
}