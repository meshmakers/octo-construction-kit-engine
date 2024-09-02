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
internal class GraphRuleEngine(ICkCacheService ckCache) : IGraphRuleEngine
{
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, entityUpdateInfoList, new List<AssociationUpdateInfo>(),
                originFileResolver, operationResult)
            .ConfigureAwait(false);
    }


    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var graphValidationResult = new GraphRuleEngineResult();

        // Validate if the associations are valid to be added/deleted based on the current database content
        var createAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Create);
        var deleteAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Delete);

        await ValidateAssociationsToCreate(session, repositoryDataSource, createAssociations, graphValidationResult, originFileResolver,
                operationResult)
            .ConfigureAwait(false);
        await ValidateAssociationsToDelete(session, repositoryDataSource, deleteAssociations, graphValidationResult, originFileResolver,
                operationResult)
            .ConfigureAwait(false);

        // Validate the consistency of the construction kit model
        await ValidateCkModel(session, repositoryDataSource, graphValidationResult, entityUpdateInfoList, associationUpdateInfoList,
            originFileResolver, operationResult).ConfigureAwait(false);

        return graphValidationResult;
    }

    // ReSharper disable once UnusedMember.Global
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, new List<IEntityUpdateInfo<RtEntity>>(), associationUpdateInfoList,
            originFileResolver, operationResult).ConfigureAwait(false);
    }

    private async Task ValidateCkModel(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        GraphRuleEngineResult graphRuleEngineResult,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        await ValidateOrigin(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList, originFileResolver,
                operationResult)
            .ConfigureAwait(false);
        await ValidateTarget(session, repositoryDataSource, entityUpdateInfoList, associationUpdateInfoList, originFileResolver,
                operationResult)
            .ConfigureAwait(false);

        // Ensure that all associations exists when creating an entity
        // Currently, the only mandatory association has multiplicity of One
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Insert))
        {
            var ckTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, entityUpdateInfo.CkTypeId);

            var inputAssociationGraphs =
                ckTypeGraph.Associations.In.All.Where(a =>
                    a.Multiplicity == MultiplicitiesDto.One);
            foreach (var inputAssociationGraph in inputAssociationGraphs)
            {
                if (!associationUpdateInfoList.Any(x =>
                        x.ModOption == AssociationModOptionsDto.Create &&
                        x.RoleId == inputAssociationGraph.CkRoleId))
                {
                    operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnCreate(
                        originFileResolver.Resolve(repositoryDataSource.TenantId),
                        repositoryDataSource.TenantId,
                        entityUpdateInfo.CkTypeId, inputAssociationGraph.CkRoleId,
                        MultiplicitiesDto.One));
                }
            }
        }

        // Delete all corresponding associations if an entity is deleted  
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Delete))
        {
            var result = await repositoryDataSource.GetRtAssociationsAsync(session,
                entityUpdateInfo.RtId ?? throw PersistenceException.RtIdNotSet(),
                GraphDirections.Any).ConfigureAwait(false);
            graphRuleEngineResult.RtAssociationsToDelete.AddRange(result);
        }
    }

    private async Task ValidateTarget(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var targetList = associationUpdateInfoList.Select(a => a.Target).Distinct();
        foreach (var targetEntityId in targetList)
        {
            var targetEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList, targetEntityId, originFileResolver,
                    operationResult)
                .ConfigureAwait(false);
            if (targetEntity == null)
            {
                operationResult.AddMessage(MessageCodes.MissingTargetEntity(originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId, targetEntityId.CkTypeId,
                    targetEntityId.RtId));
                continue;
            }

            var targetCkTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, targetEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Target == targetEntityId)
                         .GroupBy(a => a.RoleId))
            {
                var inboundTypeAssociationGraph =
                    targetCkTypeGraph.Associations.In.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (inboundTypeAssociationGraph == null)
                {
                    operationResult.AddMessage(MessageCodes.OutboundAssociationNotAllowedForCkType(
                        originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                        targetEntityId.CkTypeId, targetEntityId.RtId, associationUpdateInfosByRoleId.Key, targetCkTypeGraph.CkTypeId));
                    continue;
                }
                var originCkTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, inboundTypeAssociationGraph.OriginCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var originEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList,
                        associationUpdateInfo.Origin, originFileResolver, operationResult).ConfigureAwait(false);
                    if (originEntity != null)
                    {
                        var originTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, originEntity.GetCkTypeId());

                        if (originCkTypeGraph.CkTypeId != originTypeGraph.CkTypeId &&
                            originCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != originTypeGraph.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.AssociationNotAllowed(
                                originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                                targetEntityId.CkTypeId, targetEntityId.RtId, associationUpdateInfosByRoleId.Key));
                        }
                    }
                }

                var storedTargetAssociations = await repositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(session,
                    targetEntityId, associationUpdateInfosByRoleId.Key, GraphDirections.Inbound).ConfigureAwait(false);

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    if (storedTargetAssociations == CurrentMultiplicity.One &&
                        inboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            targetEntityId.CkTypeId, targetEntityId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedTargetAssociations == CurrentMultiplicity.One &&
                        (inboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.One ||
                         inboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            targetEntityId.CkTypeId, targetEntityId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }
            }
        }
    }

    private async Task ValidateOrigin(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var originList = associationUpdateInfoList.Select(a => a.Origin).Distinct();
        foreach (var originEntityId in originList)
        {
            var originEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList, originEntityId, originFileResolver,
                    operationResult)
                .ConfigureAwait(false);
            if (originEntity == null)
            {
                operationResult.AddMessage(MessageCodes.MissingOriginEntity(originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId,
                    originEntityId.CkTypeId, originEntityId.RtId));
                continue;
            }

            var originCkTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, originEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Origin == originEntityId)
                         .GroupBy(a => a.RoleId))
            {
                var outboundTypeAssociationGraph =
                    originCkTypeGraph.Associations.Out.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (outboundTypeAssociationGraph == null)
                {
                    operationResult.AddMessage(MessageCodes.InboundAssociationNotAllowedForCkType(
                        originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                        originEntityId.CkTypeId, originEntityId.RtId, associationUpdateInfosByRoleId.Key, originCkTypeGraph.CkTypeId));
                    continue;
                }

                var targetCkTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, outboundTypeAssociationGraph.TargetCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var targetEntity = await GetEntityAsync(session, repositoryDataSource, entityUpdateInfoList,
                        associationUpdateInfo.Target, originFileResolver, operationResult).ConfigureAwait(false);
                    if (targetEntity != null)
                    {
                        var targetTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, targetEntity.GetCkTypeId());

                        if (targetCkTypeGraph.CkTypeId != targetTypeGraph.CkTypeId &&
                            targetCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != targetTypeGraph.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.OutboundAssociationNotAllowedForCkType(
                                originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                                originEntityId.CkTypeId, originEntityId.RtId, associationUpdateInfosByRoleId.Key, targetTypeGraph.CkTypeId));
                        }
                    }
                }

                var storedOriginAssociations = await repositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(session,
                    originEntityId, associationUpdateInfosByRoleId.Key, GraphDirections.Outbound).ConfigureAwait(false);

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    if (storedOriginAssociations == CurrentMultiplicity.One &&
                        outboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            originEntityId.CkTypeId, originEntityId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedOriginAssociations == CurrentMultiplicity.One &&
                        (outboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.One ||
                         outboundTypeAssociationGraph.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            originEntityId.CkTypeId, originEntityId.RtId, associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One));
                    }
                }
            }
        }
    }

    private async Task ValidateAssociationsToDelete(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<AssociationUpdateInfo> deleteAssociations, GraphRuleEngineResult graphRuleEngineResult,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
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
                operationResult.AddMessage(MessageCodes.AssociationDoesNotExist(originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId, d.RoleId,
                    origin.CkTypeId, origin.RtId, target.CkTypeId, target.RtId));
                continue;
            }

            graphRuleEngineResult.RtAssociationsToDelete.Add(rtAssociation);
        }
    }

    private async Task ValidateAssociationsToCreate(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IEnumerable<AssociationUpdateInfo> createAssociations, GraphRuleEngineResult graphRuleEngineResult,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
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
                operationResult.AddMessage(MessageCodes.AssociationAlreadyExists(originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId,
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
        RtEntityId rtEntityId, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var rtEntity = entityUpdateInfoList.Select(x => x.RtEntity)
            .FirstOrDefault(x => x?.RtId == rtEntityId.RtId);
        // ReSharper disable once ConvertIfStatementToNullCoalescingExpression
        if (rtEntity == null)
        {
            var ckTypeGraph = ckCache.GetCkType(repositoryDataSource.TenantId, rtEntityId.CkTypeId);
            var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
            rtEntity = await collection.DocumentAsync(session, rtEntityId.RtId).ConfigureAwait(false);
        }

        if (rtEntity == null)
        {
            operationResult.AddMessage(MessageCodes.EntityNotFound(originFileResolver.Resolve(repositoryDataSource.TenantId),
                repositoryDataSource.TenantId,
                rtEntityId.CkTypeId, rtEntityId.RtId));
        }

        return rtEntity;
    }
}