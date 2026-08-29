using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     Write-side data-permission enforcement at the engine chokepoint (AB#4973): classifies every
///     entity/association change against the policy table, verifies ownership for owned-only grants
///     (batch pre-read of the stored creators) and publishes audit events for AuditOnly violations.
///     Denied changes land as errors in the operation result — the whole change set is rejected
///     atomically by the caller.
/// </summary>
internal static class DataPermissionWriteGuard
{
    private const int ForbiddenMessageNumber = 4973;

    internal static async Task CheckAsync(IRuntimeRepository runtimeRepository, ICkCacheService ckCacheService,
        IAuditEventSink? auditEventSink, IOctoSession session, RtSecurityContext securityContext,
        RtDataPolicyTable policyTable, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult)
    {
        var tenantId = runtimeRepository.TenantId;

        foreach (var typeGroup in entityUpdateInfoList.GroupBy(i => i.CkTypeId))
        {
            var access = ClassifyType(ckCacheService, tenantId, typeGroup.Key, policyTable, securityContext);
            var ownershipCheckIds = new List<OctoObjectId>();

            foreach (var item in typeGroup)
            {
                var action = item.ModOption == EntityModOptions.Delete ? RtDataAction.Delete : RtDataAction.Write;
                var enforced = action == RtDataAction.Delete ? access.EnforcedDelete : access.EnforcedWrite;
                var audit = action == RtDataAction.Delete ? access.AuditDelete : access.AuditWrite;

                switch (enforced)
                {
                    case RtDataAccessLevel.Denied:
                        AddForbidden(operationResult, typeGroup.Key, item.RtId, action);
                        continue;
                    case RtDataAccessLevel.OwnedOnly when item.ModOption != EntityModOptions.Insert &&
                                                          item.RtId != null:
                        ownershipCheckIds.Add(item.RtId.Value);
                        break;
                }

                if (enforced is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed &&
                    audit is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly)
                {
                    await PublishAuditAsync(auditEventSink, tenantId, securityContext, typeGroup.Key, action)
                        .ConfigureAwait(false);
                }
            }

            if (ownershipCheckIds.Count > 0)
            {
                await CheckOwnershipAsync(runtimeRepository, session, securityContext, typeGroup.Key,
                    ownershipCheckIds, operationResult).ConfigureAwait(false);
            }
        }

        foreach (var associationGroup in associationUpdateInfoList.GroupBy(a => a.Origin.CkTypeId))
        {
            var access = ClassifyType(ckCacheService, tenantId, associationGroup.Key, policyTable, securityContext);
            switch (access.EnforcedWrite)
            {
                case RtDataAccessLevel.Denied:
                    foreach (var association in associationGroup)
                    {
                        AddForbidden(operationResult, associationGroup.Key, association.Origin.RtId,
                            RtDataAction.Write);
                    }

                    break;
                case RtDataAccessLevel.OwnedOnly:
                    await CheckOwnershipAsync(runtimeRepository, session, securityContext, associationGroup.Key,
                        associationGroup.Select(a => a.Origin.RtId).Distinct().ToList(), operationResult)
                        .ConfigureAwait(false);
                    break;
                case RtDataAccessLevel.Open or RtDataAccessLevel.Allowed when
                    access.AuditWrite is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly:
                    await PublishAuditAsync(auditEventSink, tenantId, securityContext, associationGroup.Key,
                        RtDataAction.Write).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static TypeAccess ClassifyType(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> ckTypeId, RtDataPolicyTable policyTable, RtSecurityContext securityContext)
    {
        var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId, ckTypeId);
        return new TypeAccess(
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Write, securityContext, false),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Delete, securityContext, false),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Write, securityContext, true),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Delete, securityContext, true));
    }

    private static async Task CheckOwnershipAsync(IRuntimeRepository runtimeRepository, IOctoSession session,
        RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
        OperationResult operationResult)
    {
        foreach (var rtId in rtIds)
        {
            // Deliberately the raw document read (not the query path): the caller's read filter hides
            // foreign owned-only entities, but the ownership check must see the stored creator.
            var entity = await runtimeRepository
                .GetRtEntityByRtIdAsync(session, new RtEntityId(ckTypeId, rtId))
                .ConfigureAwait(false);
            if (entity == null)
            {
                // Ids without a stored entity are left to the normal write path (no-op semantics).
                continue;
            }

            // Legacy rows without a creator are not writable under an owned-only grant (fail closed).
            if (entity.RtCreatedBy == null || entity.RtCreatedBy != securityContext.SubjectId)
            {
                AddForbidden(operationResult, ckTypeId, entity.RtId, RtDataAction.Write);
            }
        }
    }

    private static void AddForbidden(OperationResult operationResult, RtCkId<CkTypeId> ckTypeId,
        OctoObjectId? rtId, RtDataAction action)
    {
        operationResult.AddMessage(new OperationMessage(MessageLevel.Error, $"{ckTypeId}@{rtId}",
            ForbiddenMessageNumber,
            $"Access denied: missing data permission '{action}' on '{ckTypeId.FullName}'."));
    }

    private static Task PublishAuditAsync(IAuditEventSink? auditEventSink, string tenantId,
        RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId, RtDataAction action)
    {
        return auditEventSink?.PublishAsync(new AuditEvent(tenantId, AuditEventLevel.Warning,
                   "DataPermissions.WriteViolation",
                   $"Subject '{securityContext.SubjectId}' performed '{action}' on protected type " +
                   $"'{ckTypeId.FullName}' without a grant (AuditOnly policy — not blocked)."))
               ?? Task.CompletedTask;
    }

    private sealed record TypeAccess(
        RtDataAccessLevel EnforcedWrite,
        RtDataAccessLevel EnforcedDelete,
        RtDataAccessLevel AuditWrite,
        RtDataAccessLevel AuditDelete);
}
