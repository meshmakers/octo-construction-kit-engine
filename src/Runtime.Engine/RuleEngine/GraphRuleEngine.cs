using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Messages;

namespace Meshmakers.Octo.Runtime.Engine.RuleEngine;

/// <summary>
///     Implementation of the runtime graph validation engine
/// </summary>
internal class GraphRuleEngine(ICkCacheService ckCache) : IGraphRuleEngine
{
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, entityUpdateInfoList,
                new List<AssociationUpdateInfo>(),
                originFileResolver, operationResult)
            .ConfigureAwait(false);
    }


    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var graphValidationResult = new GraphRuleEngineResult();

        var validatedAssociationUpdateInfoList = new List<AssociationUpdateInfo>();
        foreach (var associationUpdateInfo in associationUpdateInfoList)
        {
            var associationRole =
                ckCache.GetRtCkAssociationRole(repositoryDataSource.TenantId, associationUpdateInfo.RoleId);

            validatedAssociationUpdateInfoList.Add(new AssociationUpdateInfo(associationUpdateInfo.Origin,
                associationUpdateInfo.Target, associationRole.CkRoleId.ToRtCkId(), associationUpdateInfo.ModOption));
        }

        // Validate if the associations are valid to be added/deleted based on the current database content
        var createAssociations =
            validatedAssociationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Create);
        var deleteAssociations =
            validatedAssociationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Delete);

        // Checks if assoc already exists in the repository
        await ValidateAssociationsToCreate(session, repositoryDataSource, createAssociations.ToList(),
                graphValidationResult,
                originFileResolver,
                operationResult)
            .ConfigureAwait(false);

        // Checks if assoc exists in the repository, if not, it cannot be deleted...
        await ValidateAssociationsToDelete(session, repositoryDataSource, deleteAssociations, graphValidationResult,
                originFileResolver,
                operationResult)
            .ConfigureAwait(false);

        // Validate the consistency of the construction kit model
        await ValidateCkModel(session, repositoryDataSource, graphValidationResult, entityUpdateInfoList,
            validatedAssociationUpdateInfoList,
            originFileResolver, operationResult).ConfigureAwait(false);

        return graphValidationResult;
    }

    // ReSharper disable once UnusedMember.Global
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        return await ValidateAsync(session, repositoryDataSource, new List<IEntityUpdateInfo<RtEntity>>(),
            associationUpdateInfoList,
            originFileResolver, operationResult).ConfigureAwait(false);
    }

    private async Task ValidateCkModel(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        GraphRuleEngineResult graphRuleEngineResult,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        Dictionary<RtEntityId, RtEntity> originEntities = await GetEntitiesAsync(session, repositoryDataSource,
                entityUpdateInfoList,
                associationUpdateInfoList.Select(a => a.Origin).Distinct(), originFileResolver, operationResult)
            .ConfigureAwait(false);
        Dictionary<RtEntityId, RtEntity> targetEntities = await GetEntitiesAsync(session, repositoryDataSource,
                entityUpdateInfoList,
                associationUpdateInfoList.Select(a => a.Target).Distinct(), originFileResolver, operationResult)
            .ConfigureAwait(false);

        await ValidateOrigin(session, repositoryDataSource, associationUpdateInfoList, originEntities, targetEntities,
                originFileResolver, entityUpdateInfoList,
                operationResult)
            .ConfigureAwait(false);
        await ValidateTarget(session, repositoryDataSource, associationUpdateInfoList, originEntities, targetEntities,
                originFileResolver, entityUpdateInfoList,
                operationResult)
            .ConfigureAwait(false);

        // Ensure that all mandatory associations with multiplicity of One exist when creating an entity
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Insert)
                     .AsParallel())
        {
            var ckTypeGraph = ckCache.GetRtCkType(repositoryDataSource.TenantId, entityUpdateInfo.CkTypeId);

            var inputAssociationGraphs =
                ckTypeGraph.Associations.Out.All.Where(a =>
                    a.Multiplicity == MultiplicitiesDto.One).GroupBy(x => x.CkRoleId.ToRtCkId());
            foreach (var inputAssociationGraphGrouping in inputAssociationGraphs)
            {
                if (!associationUpdateInfoList.Any(x =>
                        x.ModOption == AssociationModOptionsDto.Create &&
                        x.RoleId == inputAssociationGraphGrouping.Key))
                {
                    operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnCreate(
                        originFileResolver.Resolve(repositoryDataSource.TenantId),
                        repositoryDataSource.TenantId,
                        entityUpdateInfo.CkTypeId, inputAssociationGraphGrouping.Key,
                        MultiplicitiesDto.One));
                }
            }
        }

        // Delete all corresponding associations if an entity is deleted  
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Delete)
                     .AsParallel())
        {
            var rtEntityId = new RtEntityId(entityUpdateInfo.CkTypeId,
                entityUpdateInfo.RtId ?? throw PersistenceException.RtIdNotSet());
            var result = await repositoryDataSource.GetRtAssociationsAsync(session,
                [rtEntityId], RtAssociationExtendedQueryOptions.Create(GraphDirections.Any)).ConfigureAwait(false);
            if (result.TryGetValue(rtEntityId, out var value))
            {
                graphRuleEngineResult.RtAssociationsToDelete.AddRange(value.Items);
            }
        }
    }

    private async Task ValidateTarget(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        Dictionary<RtEntityId, RtEntity> originEntities, Dictionary<RtEntityId, RtEntity> targetEntities,
        IOriginFileResolver originFileResolver, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        // Get the current multiplicity of the associations
        var targetRoles = associationUpdateInfoList.Select(s => Tuple.Create(s.Target, s.RoleId)).Distinct();
        var rtEntityRoleIdDirectionPairs = targetRoles.Select(a =>
            new RtEntityRoleIdDirectionPair(a.Item1, a.Item2, GraphDirections.Inbound));
        var storedTargetAssociationList = await repositoryDataSource.GetRtAssociationsMultiplicityAsync(
            session, rtEntityRoleIdDirectionPairs).ConfigureAwait(false);
        var storedTargetAssociations = storedTargetAssociationList.ToDictionary(a =>
                new Tuple<RtEntityId, RtCkId<CkAssociationRoleId>>(a.Pair.RtEntityId, a.Pair.CkRoleId),
            v => v.CurrentMultiplicity);

        foreach (var targetEntity in targetEntities.AsParallel())
        {
            var targetCkTypeGraph = ckCache.GetRtCkType(repositoryDataSource.TenantId, targetEntity.Key.CkTypeId);

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList
                         .Where(a => a.Target == targetEntity.Key)
                         .GroupBy(a => a.RoleId).AsParallel())
            {
                var inboundTypeAssociationGraphs =
                    targetCkTypeGraph.Associations.In.All.Where(a =>
                        a.CkRoleId.ToRtCkId() == associationUpdateInfosByRoleId.Key).ToArray();
                if (!inboundTypeAssociationGraphs.Any())
                {
                    operationResult.AddMessage(MessageCodes.OutboundAssociationNotAllowedForCkType(
                        originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                        targetEntity.Key.CkTypeId, targetEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                        targetCkTypeGraph.CkTypeId));
                    continue;
                }

                var multiplicity = inboundTypeAssociationGraphs.First().Multiplicity;

                var originCkTypeGraphs = inboundTypeAssociationGraphs.Select(ckType =>
                    ckCache.GetCkType(repositoryDataSource.TenantId, ckType.OriginCkTypeId)).ToArray();

                List<CkId<CkTypeId>> originCkTypeGraphList = [];
                originCkTypeGraphList.AddRange(originCkTypeGraphs.Select(x => x.CkTypeId));
                originCkTypeGraphList.AddRange(
                    originCkTypeGraphs.SelectMany(x => x.DerivedTypes.Select(y => y.InheritorCkTypeId)));

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    if (originEntities.TryGetValue(associationUpdateInfo.Origin, out var originEntity))
                    {
                        var originTypeGraph =
                            ckCache.GetRtCkType(repositoryDataSource.TenantId, originEntity.GetRtCkTypeId());

                        if (!originCkTypeGraphList.Contains(originTypeGraph.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.AssociationNotAllowed(
                                originFileResolver.Resolve(repositoryDataSource.TenantId),
                                repositoryDataSource.TenantId,
                                targetEntity.Key.CkTypeId, targetEntity.Key.RtId, associationUpdateInfosByRoleId.Key));
                        }
                    }
                }

                var currentMultiplicity = CurrentMultiplicity.Zero;
                if (storedTargetAssociations.TryGetValue(
                        new Tuple<RtEntityId, RtCkId<CkAssociationRoleId>>(targetEntity.Key,
                            associationUpdateInfosByRoleId.Key),
                        out var multiplicityValue))
                {
                    currentMultiplicity = multiplicityValue;
                }

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    // Check if any of the origin entities being deleted for these associations
                    var isAnyOriginEntityBeingDeleted = associationUpdateInfosByRoleId
                        .Where(a => a.ModOption == AssociationModOptionsDto.Delete)
                        .Any(assoc => entityUpdateInfoList.Any(e =>
                            e.ModOption == EntityModOptions.Delete &&
                            e.RtId == assoc.Origin.RtId));

                    if (!isAnyOriginEntityBeingDeleted &&
                        currentMultiplicity == CurrentMultiplicity.One &&
                        multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            targetEntity.Key.CkTypeId, targetEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                            MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (currentMultiplicity == CurrentMultiplicity.One &&
                        (multiplicity == MultiplicitiesDto.One ||
                         multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            targetEntity.Key.CkTypeId, targetEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                            MultiplicitiesDto.One));
                    }
                }
            }
        }
    }


    private async Task ValidateOrigin(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        Dictionary<RtEntityId, RtEntity> originEntities, Dictionary<RtEntityId, RtEntity> targetEntities,
        IOriginFileResolver originFileResolver, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        // Get the current multiplicity of the associations
        var originRoles = associationUpdateInfoList.Select(s => Tuple.Create(s.Origin, s.RoleId)).Distinct();
        var rtEntityRoleIdDirectionPairs = originRoles.Select(a =>
            new RtEntityRoleIdDirectionPair(a.Item1, a.Item2, GraphDirections.Outbound));
        var storedOriginAssociationList = await repositoryDataSource.GetRtAssociationsMultiplicityAsync(
            session, rtEntityRoleIdDirectionPairs).ConfigureAwait(false);
        var storedOriginAssociations = storedOriginAssociationList.ToDictionary(a =>
                new Tuple<RtEntityId, RtCkId<CkAssociationRoleId>>(a.Pair.RtEntityId, a.Pair.CkRoleId),
            v => v.CurrentMultiplicity);

        foreach (var originEntity in originEntities.AsParallel())
        {
            var originCkTypeGraph = ckCache.GetRtCkType(repositoryDataSource.TenantId, originEntity.Key.CkTypeId);

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList
                         .Where(a => a.Origin == originEntity.Key)
                         .GroupBy(a => a.RoleId).AsParallel())
            {
                var outboundTypeAssociationGraphs =
                    originCkTypeGraph.Associations.Out.All
                        .Where(a => a.CkRoleId.ToRtCkId() == associationUpdateInfosByRoleId.Key)
                        .ToArray();
                if (!outboundTypeAssociationGraphs.Any())
                {
                    operationResult.AddMessage(MessageCodes.InboundAssociationNotAllowedForCkType(
                        originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                        originEntity.Key.CkTypeId, originEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                        originCkTypeGraph.CkTypeId));
                    continue;
                }

                var multiplicity = outboundTypeAssociationGraphs.First().Multiplicity;

                var targetCkTypeGraphs = outboundTypeAssociationGraphs.Select(g =>
                    ckCache.GetCkType(repositoryDataSource.TenantId, g.TargetCkTypeId)).ToArray();

                List<CkId<CkTypeId>> targetCkTypeGraphList = [];
                targetCkTypeGraphList.AddRange(targetCkTypeGraphs.Select(x => x.CkTypeId));
                targetCkTypeGraphList.AddRange(
                    targetCkTypeGraphs.SelectMany(x => x.DerivedTypes.Select(y => y.InheritorCkTypeId)));

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    if (targetEntities.TryGetValue(associationUpdateInfo.Target, out var targetEntity))
                    {
                        var targetTypeGraph =
                            ckCache.GetRtCkType(repositoryDataSource.TenantId, targetEntity.GetRtCkTypeId());

                        if (!targetCkTypeGraphList.Contains(targetTypeGraph.CkTypeId))
                        {
                            operationResult.AddMessage(MessageCodes.OutboundAssociationNotAllowedForCkType(
                                originFileResolver.Resolve(repositoryDataSource.TenantId),
                                repositoryDataSource.TenantId,
                                originEntity.Key.CkTypeId, originEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                                targetTypeGraph.CkTypeId));
                        }
                    }
                }

                var currentMultiplicity = CurrentMultiplicity.Zero;
                if (storedOriginAssociations.TryGetValue(
                        new Tuple<RtEntityId, RtCkId<CkAssociationRoleId>>(originEntity.Key,
                            associationUpdateInfosByRoleId.Key),
                        out var multiplicityValue))
                {
                    currentMultiplicity = multiplicityValue;
                }

                var createCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Create);
                var deleteCount =
                    associationUpdateInfosByRoleId.Count(x => x.ModOption == AssociationModOptionsDto.Delete);
                var changeDelta = createCount - deleteCount;

                if (changeDelta < 0)
                {
                    // Check if the origin entity is being deleted - if so, don't validate cardinality
                    var isOriginEntityBeingDeleted = entityUpdateInfoList.Any(e =>
                        e.ModOption == EntityModOptions.Delete &&
                        e.RtId == originEntity.Key.RtId);

                    if (!isOriginEntityBeingDeleted &&
                        currentMultiplicity == CurrentMultiplicity.One &&
                        multiplicity == MultiplicitiesDto.One)
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnDelete(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            originEntity.Key.CkTypeId, originEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                            MultiplicitiesDto.One));
                    }
                }

                if (changeDelta > 0)
                {
                    if (currentMultiplicity == CurrentMultiplicity.One &&
                        (multiplicity == MultiplicitiesDto.One ||
                         multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        operationResult.AddMessage(MessageCodes.AssociationCardinalityViolationOnModification(
                            originFileResolver.Resolve(repositoryDataSource.TenantId), repositoryDataSource.TenantId,
                            originEntity.Key.CkTypeId, originEntity.Key.RtId, associationUpdateInfosByRoleId.Key,
                            MultiplicitiesDto.One));
                    }
                }
            }
        }
    }

    /// <summary>
    /// This method validates the associations to be deleted. It checks if the association exists in the database.
    /// </summary>
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
                operationResult.AddMessage(MessageCodes.AssociationDoesNotExist(
                    originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId, d.RoleId,
                    origin.CkTypeId, origin.RtId, target.CkTypeId, target.RtId));
                continue;
            }

            graphRuleEngineResult.RtAssociationsToDelete.Add(rtAssociation);
        }
    }

    /// <summary>
    /// This method validates the associations to be created. It checks if the association already exists in the database.
    /// </summary>
    private async Task ValidateAssociationsToCreate(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        IList<AssociationUpdateInfo> createAssociations, GraphRuleEngineResult graphRuleEngineResult,
        IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var rtOriginTargetPairs = createAssociations.Select(a => new RtOriginTargetPair(
            new RtEntityId(a.Origin.CkTypeId, a.Origin.RtId),
            new RtEntityId(a.Target.CkTypeId, a.Target.RtId),
            a.RoleId)).ToList();

        var rtAssociation = await repositoryDataSource.GetRtAssociationsAsync(session,
            rtOriginTargetPairs, RtAssociationExtendedQueryOptions.Create(GraphDirections.Any)).ConfigureAwait(false);

        var rtAssociationDictionary = rtAssociation.ToDictionary(x => new RtOriginTargetPair(
            new RtEntityId(x.OriginCkTypeId, x.OriginRtId),
            new RtEntityId(x.TargetCkTypeId, x.TargetRtId), x.AssociationRoleId!));

        foreach (var associationUpdateInfo in createAssociations)
        {
            var rtOriginTargetPair = new RtOriginTargetPair(
                new RtEntityId(associationUpdateInfo.Origin.CkTypeId, associationUpdateInfo.Origin.RtId),
                new RtEntityId(associationUpdateInfo.Target.CkTypeId, associationUpdateInfo.Target.RtId),
                associationUpdateInfo.RoleId);

            if (rtAssociationDictionary.ContainsKey(rtOriginTargetPair))
            {
                operationResult.AddMessage(MessageCodes.AssociationAlreadyExists(
                    originFileResolver.Resolve(repositoryDataSource.TenantId),
                    repositoryDataSource.TenantId,
                    associationUpdateInfo.RoleId,
                    rtOriginTargetPair.Origin.CkTypeId, rtOriginTargetPair.Origin.RtId,
                    rtOriginTargetPair.Target.CkTypeId, rtOriginTargetPair.Target.RtId));
                continue;
            }

            graphRuleEngineResult.RtAssociationsToCreate.Add(repositoryDataSource.CreateTransientRtAssociation(
                new RtEntityId(associationUpdateInfo.Origin.CkTypeId, associationUpdateInfo.Origin.RtId),
                associationUpdateInfo.RoleId,
                new RtEntityId(associationUpdateInfo.Target.CkTypeId, associationUpdateInfo.Target.RtId)));
        }
    }


    private async Task<Dictionary<RtEntityId, RtEntity>> GetEntitiesAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IEnumerable<RtEntityId> rtEntityIds, IOriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var result = new Dictionary<RtEntityId, RtEntity>();
        var dbRequests = new List<RtEntityId>();

        foreach (var rtEntityId in rtEntityIds)
        {
            var rtEntity = entityUpdateInfoList.Select(x => x.RtEntity)
                .FirstOrDefault(x => x?.RtId == rtEntityId.RtId);
            if (rtEntity != null)
            {
                result.Add(rtEntityId, rtEntity);
                continue;
            }

            dbRequests.Add(rtEntityId);
        }

        if (dbRequests.Count == 0)
        {
            return result;
        }

        foreach (var rtEntityIdGrouping in dbRequests.GroupBy(x => x.CkTypeId))
        {
            var ckTypeGraph = ckCache.GetRtCkType(repositoryDataSource.TenantId, rtEntityIdGrouping.Key);
            var collection = repositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
            var rtEntities = await collection.DocumentsAsync(session, rtEntityIdGrouping.Select(x => x.RtId))
                .ConfigureAwait(false);

            var d = rtEntities.ToDictionary(x => x.RtId, x => x);
            foreach (var rtEntity in rtEntityIdGrouping)
            {
                if (!d.TryGetValue(rtEntity.RtId, out var rtEntityValue))
                {
                    operationResult.AddMessage(MessageCodes.EntityNotFound(
                        originFileResolver.Resolve(repositoryDataSource.TenantId),
                        repositoryDataSource.TenantId,
                        rtEntity.CkTypeId, rtEntity.RtId));
                }
                else
                {
                    result.Add(rtEntity, rtEntityValue);
                }
            }
        }

        return result;
    }
}