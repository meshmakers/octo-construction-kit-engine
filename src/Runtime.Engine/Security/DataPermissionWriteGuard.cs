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
///     atomically by the caller. Opt-in blueprint-lock restriction (AB#6384): see
///     <see cref="CheckBlueprintLockAsync" />.
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
                await CheckOwnershipAsync(runtimeRepository, ckCacheService, session, securityContext, typeGroup.Key,
                    ownershipCheckIds, operationResult).ConfigureAwait(false);
            }

            if (access.BlueprintLock != RtBlueprintLockProtection.None)
            {
                // AB#6384: only opted-in types reach this; every other type costs nothing here.
                await CheckBlueprintLockAsync(runtimeRepository, auditEventSink, securityContext, typeGroup.Key,
                    access.BlueprintLock, typeGroup.ToList(), operationResult).ConfigureAwait(false);
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
                    await CheckOwnershipAsync(runtimeRepository, ckCacheService, session, securityContext,
                        associationGroup.Key, associationGroup.Select(a => a.Origin.RtId).Distinct().ToList(),
                        operationResult).ConfigureAwait(false);
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
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Delete, securityContext, true),
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(policyTable, selfAndBase, securityContext));
    }

    /// <summary>
    ///     Blueprint-lock restriction (AB#6384) for the items of one opted-in type: updates/replaces/deletes
    ///     of stored entities with <c>RtBlueprintLocked = true</c> are refused, and the blueprint bookkeeping
    ///     attributes cannot be set on insert or changed on update/replace. The stored state comes from ONE
    ///     batched read by rtId for all non-insert items of the type, taken on a system session: the caller's
    ///     own read filter must not hide a locked entity from its own lock check (fail closed). AuditOnly
    ///     mode publishes one audit event per type and lets the change through. Associations of locked
    ///     entities are out of scope in v1.
    /// </summary>
    private static async Task CheckBlueprintLockAsync(IRuntimeRepository runtimeRepository,
        IAuditEventSink? auditEventSink, RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId,
        RtBlueprintLockProtection mode, IReadOnlyList<IEntityUpdateInfo<RtEntity>> items,
        OperationResult operationResult)
    {
        var storedIds = items.Where(i => i.ModOption != EntityModOptions.Insert && i.RtId != null)
            .Select(i => i.RtId!.Value).Distinct().ToList();

        var stored = new Dictionary<OctoObjectId, RtEntity>();
        if (storedIds.Count > 0)
        {
            using var systemSession = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);
            var result = await runtimeRepository.GetRtEntitiesByIdAsync(systemSession, ckTypeId, storedIds,
                RtEntityQueryOptions.Create(), take: storedIds.Count).ConfigureAwait(false);
            foreach (var entity in result.Items)
            {
                stored[entity.RtId] = entity;
            }
        }

        var violations = new List<OperationMessage>();
        foreach (var item in items)
        {
            if (item.ModOption == EntityModOptions.Insert)
            {
                var attributeNames = ChangedProtectedAttributes(null, item.RtEntity, EntityModOptions.Insert);
                if (attributeNames.Count > 0)
                {
                    violations.Add(ProtectedAttributesMessage(ckTypeId, item.RtId, attributeNames));
                }

                continue;
            }

            // Ids without a stored entity are left to the normal write path (no-op semantics).
            if (item.RtId == null || !stored.TryGetValue(item.RtId.Value, out var storedEntity))
            {
                continue;
            }

            if (IsBlueprintLocked(storedEntity))
            {
                violations.Add(LockedMessage(ckTypeId, item.RtId, item.ModOption));
            }
            else if (item.ModOption != EntityModOptions.Delete)
            {
                var attributeNames = ChangedProtectedAttributes(storedEntity, item.RtEntity, item.ModOption);
                if (attributeNames.Count > 0)
                {
                    violations.Add(ProtectedAttributesMessage(ckTypeId, item.RtId, attributeNames));
                }
            }
        }

        if (violations.Count == 0)
        {
            return;
        }

        if (mode == RtBlueprintLockProtection.Enforce)
        {
            foreach (var violation in violations)
            {
                operationResult.AddMessage(violation);
            }

            return;
        }

        if (auditEventSink != null)
        {
            await auditEventSink.PublishAsync(new AuditEvent(runtimeRepository.TenantId, AuditEventLevel.Warning,
                "DataPermissions.BlueprintLockViolation",
                $"Subject '{securityContext.SubjectId}' changed {violations.Count} blueprint-locked entity(ies) " +
                $"or blueprint attributes of '{ckTypeId.SemanticVersionedFullName}' (AuditOnly policy — not blocked).")
            ).ConfigureAwait(false);
        }
    }

    internal static bool IsBlueprintLocked(RtEntity entity)
    {
        return entity.GetAttributeValueOrDefault(RtBlueprintLockProtectionNames.LockedAttributeName) is true;
    }

    /// <summary>
    ///     Names of the protected attributes the change would set (insert) or change (update/replace)
    ///     relative to the stored entity. An update only touches attributes present in the payload; a replace
    ///     rewrites all, so an omitted protected attribute counts as removed.
    /// </summary>
    private static List<string> ChangedProtectedAttributes(RtEntity? stored, RtEntity? payload,
        EntityModOptions modOption)
    {
        var changed = new List<string>();
        foreach (var name in RtBlueprintLockProtectionNames.ProtectedAttributeNames)
        {
            var present = payload != null && payload.Attributes.ContainsKey(name);
            if (!present && modOption != EntityModOptions.Replace)
            {
                continue;
            }

            var newValue = present ? payload!.Attributes[name] : null;
            var oldValue = stored?.GetAttributeValueOrDefault(name);
            if (!ProtectedValuesEqual(name, oldValue, newValue))
            {
                changed.Add(name);
            }
        }

        return changed;
    }

    private static bool ProtectedValuesEqual(string attributeName, object? oldValue, object? newValue)
    {
        if (attributeName == RtBlueprintLockProtectionNames.LockedAttributeName)
        {
            // absent == false: a form round trip of "false" must not count as a change
            return (oldValue is true) == (newValue is true);
        }

        if (oldValue == null || newValue == null)
        {
            return oldValue == null && newValue == null;
        }

        if (oldValue is DateTime oldDate && newValue is DateTime newDate)
        {
            return oldDate.ToUniversalTime() == newDate.ToUniversalTime();
        }

        return Equals(oldValue, newValue) || oldValue.ToString() == newValue.ToString();
    }

    internal static OperationMessage LockedMessage(RtCkId<CkTypeId> ckTypeId, OctoObjectId? rtId,
        EntityModOptions modOption)
    {
        var verb = modOption == EntityModOptions.Delete ? "deleted" : "changed";
        return new OperationMessage(MessageLevel.Error, $"{ckTypeId}@{rtId}",
            RtBlueprintLockProtectionNames.ForbiddenMessageNumber,
            $"Access denied: entity '{ckTypeId.SemanticVersionedFullName}@{rtId}' is locked by blueprint and cannot be {verb}.");
    }

    private static OperationMessage ProtectedAttributesMessage(RtCkId<CkTypeId> ckTypeId, OctoObjectId? rtId,
        IReadOnlyCollection<string> attributeNames)
    {
        return new OperationMessage(MessageLevel.Error, $"{ckTypeId}@{rtId}",
            RtBlueprintLockProtectionNames.ForbiddenMessageNumber,
            $"Access denied: attribute(s) {string.Join(", ", attributeNames)} of " +
            $"'{ckTypeId.SemanticVersionedFullName}@{rtId}' are managed by blueprints (locked by blueprint) and " +
            "cannot be set or changed by users.");
    }

    private static async Task CheckOwnershipAsync(IRuntimeRepository runtimeRepository,
        ICkCacheService ckCacheService, IOctoSession session, RtSecurityContext securityContext,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds, OperationResult operationResult)
    {
        // AB#4978: a CK-model-declared owner attribute replaces the stamped creator as the owner.
        var ownerAttributePath = RtDataPermissionCkTypeHelper.GetEffectiveOwnerAttributePath(ckCacheService,
            runtimeRepository.TenantId, ckTypeId);

        foreach (var rtId in rtIds)
        {
            // Deliberately the raw document read (not the query path): the caller's read filter hides
            // foreign owned-only entities, but the ownership check must see the stored owner.
            var entity = await runtimeRepository
                .GetRtEntityByRtIdAsync(session, new RtEntityId(ckTypeId, rtId))
                .ConfigureAwait(false);
            if (entity == null)
            {
                // Ids without a stored entity are left to the normal write path (no-op semantics).
                continue;
            }

            // Rows without an owner value are not writable under an owned-only grant (fail closed).
            var owner = ownerAttributePath == null
                ? entity.RtCreatedBy
                : entity.GetAttributeValueByAccessPath(ckCacheService, runtimeRepository.TenantId,
                    ownerAttributePath) as string;
            if (owner == null || owner != securityContext.SubjectId)
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
            $"Access denied: missing data permission '{action}' on '{ckTypeId.SemanticVersionedFullName}'."));
    }

    private static Task PublishAuditAsync(IAuditEventSink? auditEventSink, string tenantId,
        RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId, RtDataAction action)
    {
        return auditEventSink?.PublishAsync(new AuditEvent(tenantId, AuditEventLevel.Warning,
                   "DataPermissions.WriteViolation",
                   $"Subject '{securityContext.SubjectId}' performed '{action}' on protected type " +
                   $"'{ckTypeId.SemanticVersionedFullName}' without a grant (AuditOnly policy — not blocked)."))
               ?? Task.CompletedTask;
    }

    private sealed record TypeAccess(
        RtDataAccessLevel EnforcedWrite,
        RtDataAccessLevel EnforcedDelete,
        RtDataAccessLevel AuditWrite,
        RtDataAccessLevel AuditDelete,
        RtBlueprintLockProtection BlueprintLock);
}
