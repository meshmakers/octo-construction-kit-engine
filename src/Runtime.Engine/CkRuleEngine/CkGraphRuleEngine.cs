using Meshmakers.Octo.Common.Shared.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.SystematizedData.Persistence.DataAccess;
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel.CkRuleEngine;

public class CkGraphRuleEngine : ICkGraphRuleEngine
{
    private readonly ICkCacheService _ckCache;
    private readonly ITenantRepositoryInternal _tenantRepository;

    public CkGraphRuleEngine(ICkCacheService ckCache, ITenantRepositoryInternal tenantRepository)
    {
        _ckCache = ckCache;
        _tenantRepository = tenantRepository;
    }

    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList)
    {
        return await ValidateAsync(session, entityUpdateInfoList, new List<AssociationUpdateInfo>());
    }


    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        var graphValidationResult = new GraphRuleEngineResult();

        // Validate if the associations are valid to be added/deleted based on the current database content
        var createAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Create);
        var deleteAssociations = associationUpdateInfoList.Where(x => x.ModOption == AssociationModOptionsDto.Delete);

        await ValidateAssociationsToCreate(session, createAssociations, graphValidationResult);
        await ValidateAssociationsToDelete(session, deleteAssociations, graphValidationResult);

        // Validate the consistency of the construction kit model
        await ValidateCkModel(session, graphValidationResult, entityUpdateInfoList, associationUpdateInfoList);

        return graphValidationResult;
    }

    // ReSharper disable once UnusedMember.Global
    public async Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        return await ValidateAsync(session, new List<EntityUpdateInfo>(), associationUpdateInfoList);
    }

    private async Task ValidateCkModel(IOctoSession session, GraphRuleEngineResult graphRuleEngineResult,
        IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        await ValidateOrigin(session, entityUpdateInfoList, associationUpdateInfoList);
        await ValidateTarget(session, entityUpdateInfoList, associationUpdateInfoList);

        // Ensure that all associations exists when creating an entity
        // Currently, the only mandatory association has multiplicity of One
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Create))
        {
            var cacheItem = _ckCache.GetCkType(_tenantRepository.TenantId, entityUpdateInfo.RtEntity.GetCkTypeId());

            var inboundAssociationCacheItems =
                cacheItem.Associations.In.All.Where(a =>
                    a.Multiplicity == MultiplicitiesDto.One);
            foreach (var inboundAssociationCacheItem in inboundAssociationCacheItems)
            {
                if (!associationUpdateInfoList.Any(x =>
                        x.ModOption == AssociationModOptionsDto.Create &&
                        x.RoleId == inboundAssociationCacheItem.CkRoleId))
                {
                    throw RuleViolationException.AssociationCardinalityViolationOnCreate(
                        inboundAssociationCacheItem.CkRoleId, MultiplicitiesDto.One,
                        entityUpdateInfo.RtEntity.ToRtEntityId());
                }
            }
        }

        // Delete all corresponding associations if an entity is deleted  
        foreach (var entityUpdateInfo in entityUpdateInfoList.Where(x => x.ModOption == EntityModOptions.Delete))
        {
            var result = await _tenantRepository.GetRtAssociationsAsync(session,
                entityUpdateInfo.RtEntity.RtId.ToString(), GraphDirections.Any);
            graphRuleEngineResult.RtAssociationsToDelete.AddRange(result);
        }
    }

    private async Task ValidateTarget(IOctoSession session, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        var targetList = associationUpdateInfoList.Select(a => a.Target).Distinct();
        foreach (var targetRtId in targetList)
        {
            var targetEntity = await GetEntity(session, entityUpdateInfoList, targetRtId);
            if (targetEntity == null)
            {
                throw RuleViolationException.MissingTargetEntity(targetRtId);
            }

            var targetCacheItem = _ckCache.GetCkType(_tenantRepository.TenantId, targetEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Target == targetRtId)
                         .GroupBy(a => a.RoleId))
            {
                var inboundAssociationCacheItem = targetCacheItem.Associations.In.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (inboundAssociationCacheItem == null)
                {
                    throw RuleViolationException.AssociationNotAllowed(
                        associationUpdateInfosByRoleId.Key, targetRtId);

                }
                var targetCkTypeGraph = _ckCache.GetCkType(_tenantRepository.TenantId, inboundAssociationCacheItem.TargetCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var originEntity = await GetEntity(session, entityUpdateInfoList, associationUpdateInfo.Origin);
                    var originCacheItem = _ckCache.GetCkType(_tenantRepository.TenantId, originEntity.GetCkTypeId());

                    if (targetCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != originCacheItem.CkTypeId))
                    {
                        throw RuleViolationException.AssociationNotAllowed(
                            associationUpdateInfosByRoleId.Key, targetRtId);
                    }
                }

                var storedTargetAssociations = await _tenantRepository.GetCurrentRtAssociationMultiplicityAsync(session,
                    targetRtId, associationUpdateInfosByRoleId.Key, GraphDirections.Inbound);

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
                        throw RuleViolationException.AssociationCardinalityViolationOnDelete(
                            associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One, targetRtId);
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedTargetAssociations == CurrentMultiplicity.One &&
                        (inboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One ||
                         inboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        throw RuleViolationException.AssociationCardinalityViolationOnModification(
                            associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One, targetRtId);
                    }
                }
            }
        }
    }

    private async Task ValidateOrigin(IOctoSession session, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        var originList = associationUpdateInfoList.Select(a => a.Origin).Distinct();
        foreach (var originRtId in originList)
        {
            var originEntity = await GetEntity(session, entityUpdateInfoList, originRtId);
            if (originEntity == null)
            {
                throw RuleViolationException.MissingOriginEntity(originRtId);
            }

            var originCacheItem = _ckCache.GetCkType(_tenantRepository.TenantId, originEntity.GetCkTypeId());

            foreach (var associationUpdateInfosByRoleId in associationUpdateInfoList.Where(a => a.Origin == originRtId)
                         .GroupBy(a => a.RoleId))
            {
                var outboundAssociationCacheItem = originCacheItem.Associations.Out.All.FirstOrDefault(a => a.CkRoleId == associationUpdateInfosByRoleId.Key);
                if (outboundAssociationCacheItem == null)
                {
                    throw RuleViolationException.InboundAssociationNotAllowedForCkType(associationUpdateInfosByRoleId.Key, originRtId, originCacheItem.CkTypeId);
                }
                
                var targetCkTypeGraph = _ckCache.GetCkType(_tenantRepository.TenantId, outboundAssociationCacheItem.TargetCkTypeId);

                foreach (var associationUpdateInfo in associationUpdateInfosByRoleId)
                {
                    var targetEntity = await GetEntity(session, entityUpdateInfoList, associationUpdateInfo.Target);
                    var targetCacheItem = _ckCache.GetCkType(_tenantRepository.TenantId, targetEntity.GetCkTypeId());

                    if (targetCkTypeGraph.DerivedTypes.All(x => x.InheritorCkTypeId != targetCacheItem.CkTypeId))
                    {
                        throw RuleViolationException.OutboundAssociationNotAllowedForCkType(associationUpdateInfosByRoleId.Key, originRtId, targetCacheItem.CkTypeId);
                    }
                }

                var storedOriginAssociations = await _tenantRepository.GetCurrentRtAssociationMultiplicityAsync(session,
                    originRtId, associationUpdateInfosByRoleId.Key, GraphDirections.Outbound);

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
                        throw RuleViolationException.AssociationCardinalityViolationOnDelete(associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One, originRtId);
                    }
                }

                if (changeDelta > 0)
                {
                    if (storedOriginAssociations == CurrentMultiplicity.One &&
                        (outboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.One ||
                         outboundAssociationCacheItem.Multiplicity == MultiplicitiesDto.ZeroOrOne))
                    {
                        throw RuleViolationException.AssociationCardinalityViolationOnModification(associationUpdateInfosByRoleId.Key, MultiplicitiesDto.One, originRtId);
                    }
                }
            }
        }
    }

    private async Task ValidateAssociationsToDelete(IOctoSession session,
        IEnumerable<AssociationUpdateInfo> deleteAssociations, GraphRuleEngineResult graphRuleEngineResult)
    {
        foreach (var d in deleteAssociations)
        {
            var origin = d.Origin;
            var target = d.Target;

            var rtAssociation = await _tenantRepository.GetRtAssociationOrDefaultAsync(session,
                origin,
                target,
                d.RoleId);
            if (rtAssociation == null)
            {
                throw new OperationFailedException(
                    $"Association from '{origin}' to '{target}' in role '{d.RoleId}' does not exist");
            }

            graphRuleEngineResult.RtAssociationsToDelete.Add(rtAssociation);
        }
    }

    private async Task ValidateAssociationsToCreate(IOctoSession session,
        IEnumerable<AssociationUpdateInfo> createAssociations, GraphRuleEngineResult graphRuleEngineResult)
    {
        foreach (var associationUpdateInfo in createAssociations)
        {
            var origin = associationUpdateInfo.Origin;
            var target = associationUpdateInfo.Target;

            var rtAssociation = await _tenantRepository.GetRtAssociationOrDefaultAsync(session,
                origin,
                target,
                associationUpdateInfo.RoleId);
            if (rtAssociation != null)
            {
                throw new OperationFailedException(
                    $"Association from '{origin}' to '{target}' in role '{associationUpdateInfo.RoleId}' already exits.");
            }

            graphRuleEngineResult.RtAssociationsToCreate.Add(_tenantRepository.CreateTransientRtAssociation(
                new RtEntityId(associationUpdateInfo.Origin.CkTypeId, associationUpdateInfo.Origin.RtId),
                associationUpdateInfo.RoleId,
                new RtEntityId(associationUpdateInfo.Target.CkTypeId, associationUpdateInfo.Target.RtId)));

            // graphRuleEngineResult.RtAssociationsToCreate.Add(new RtAssociation
            // {
            //     OriginRtId = associationUpdateInfo.Origin.RtId,
            //     OriginCkTypeId = associationUpdateInfo.Origin.CkTypeId,
            //     TargetRtId = associationUpdateInfo.Target.RtId,
            //     TargetCkTypeId = associationUpdateInfo.Target.CkTypeId,
            //     AssociationRoleId = associationUpdateInfo.RoleId
            // });
        }
    }


    private async Task<RtEntity> GetEntity(IOctoSession session, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList,
        RtEntityId rtEntityId)
    {
        var rtEntity = entityUpdateInfoList.Select(x => x.RtEntity)
            .FirstOrDefault(x => x.RtId == rtEntityId.RtId);
        // ReSharper disable once ConvertIfStatementToNullCoalescingExpression
        if (rtEntity == null)
        {
            rtEntity = await _tenantRepository.GetRtEntityByRtIdAsync(session, rtEntityId);
        }

        if (rtEntity == null)
        {
            throw new OperationFailedException(
                $"Entity '{rtEntityId}' does not exist");
        }

        return rtEntity;
    }
}