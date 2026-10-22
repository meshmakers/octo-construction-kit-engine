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
///     <see cref="CheckBlueprintLockAsync" />. Visibility (AB#7126): an update, replace or delete of an
///     existing entity (and an association change at its origin) is first tested against the caller's read
///     visibility, with the same rules as <see cref="RtDataPermissionEvaluator" />; an entity the caller
///     cannot see is refused as if it did not exist (see <see cref="CheckVisibilityAsync" />). Inserts have no
///     visibility question, nor do the associations whose origin is inserted by the same change set.
/// </summary>
internal static class DataPermissionWriteGuard
{
    private const int ForbiddenMessageNumber = 4973;

    internal static async Task CheckAsync(IRuntimeRepository runtimeRepository, ICkCacheService ckCacheService,
        IAuditEventSink? auditEventSink, IOctoSession session, RtSecurityContext securityContext,
        RtDataPolicyTable policyTable, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult,
        List<NotVisibleChange>? notVisible = null)
    {
        var tenantId = runtimeRepository.TenantId;
        var accessByType = new Dictionary<RtCkId<CkTypeId>, DataPermissionRules.TypeAccess>();
        var storedEntities = new StoredEntityCache(runtimeRepository, session);

        DataPermissionRules.TypeAccess AccessOf(RtCkId<CkTypeId> ckTypeId)
        {
            if (!accessByType.TryGetValue(ckTypeId, out var cached))
            {
                cached = DataPermissionRules.ClassifyType(ckCacheService, tenantId, ckTypeId, policyTable,
                    securityContext);
                accessByType[ckTypeId] = cached;
            }

            return cached;
        }

        // AB#7126: visibility comes first. What the read filter hides does not exist for the caller, so it
        // is answered before (and instead of) any write/delete decision, which would hint that it exists.
        var visibilityAuditTypes = new HashSet<RtCkId<CkTypeId>>();
        if (await CheckVisibilityAsync(ckCacheService, securityContext, tenantId, AccessOf, entityUpdateInfoList,
                associationUpdateInfoList, storedEntities, operationResult, notVisible, visibilityAuditTypes)
            .ConfigureAwait(false))
        {
            return;
        }

        var writeAuditTypes = new HashSet<RtCkId<CkTypeId>>();

        foreach (var typeGroup in entityUpdateInfoList.GroupBy(i => i.CkTypeId))
        {
            var access = AccessOf(typeGroup.Key);
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
                    writeAuditTypes.Add(typeGroup.Key);
                    await DataPermissionRules.PublishWriteAuditAsync(auditEventSink, tenantId, securityContext, typeGroup.Key, action)
                        .ConfigureAwait(false);
                }
            }

            if (ownershipCheckIds.Count > 0)
            {
                await CheckOwnershipAsync(runtimeRepository, ckCacheService, storedEntities, securityContext,
                    typeGroup.Key, ownershipCheckIds, operationResult).ConfigureAwait(false);
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
            var access = AccessOf(associationGroup.Key);
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
                    await CheckOwnershipAsync(runtimeRepository, ckCacheService, storedEntities, securityContext,
                        associationGroup.Key, associationGroup.Select(a => a.Origin.RtId).Distinct().ToList(),
                        operationResult).ConfigureAwait(false);
                    break;
                case RtDataAccessLevel.Open or RtDataAccessLevel.Allowed when
                    access.AuditWrite is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly:
                    writeAuditTypes.Add(associationGroup.Key);
                    await DataPermissionRules.PublishWriteAuditAsync(auditEventSink, tenantId, securityContext, associationGroup.Key,
                        RtDataAction.Write).ConfigureAwait(false);
                    break;
            }
        }

        // AuditOnly read policy that would hide an existing entity from this write: one audit event per
        // type, never a refusal — and none when the write audit of the same type already reported.
        if (!operationResult.HasErrors && !operationResult.HasFatalErrors)
        {
            foreach (var ckTypeId in visibilityAuditTypes.Where(t => !writeAuditTypes.Contains(t)))
            {
                await DataPermissionRules.PublishWriteVisibilityAuditAsync(auditEventSink, tenantId, securityContext,
                    ckTypeId, RtDataAction.Write).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     One change of an existing entity (or an association at its origin) that the caller may not see
    ///     (AB#7126). The repository turns the first one into its own "entity not found" exception, so a
    ///     hidden entity and a missing entity are indistinguishable for the caller.
    /// </summary>
    internal sealed record NotVisibleChange(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId, RtDataAction Action);

    /// <summary>
    ///     Message number of the placeholder error the guard adds for a <see cref="NotVisibleChange" />. The
    ///     text carries no hint that the entity exists.
    /// </summary>
    internal const int NotVisibleMessageNumber = 7126;

    /// <summary>
    ///     Visibility rule of the write funnel (AB#7126), identical to the first step of
    ///     <see cref="RtDataPermissionEvaluator" />: for an update, replace or delete of an existing entity,
    ///     and for an association change at its origin, the Read classification of the entity decides first.
    ///     No read grant on the (protected) type, or an owned-only read grant and the caller is not the owner,
    ///     means the entity is invisible and the change is refused as not found; an id without a stored entity
    ///     is refused the same way as an invisible one when the caller's read access is restricted (the
    ///     evaluator's <c>EntityNotFound</c>), so existence is never revealed; with unrestricted read it is
    ///     left to the normal write path, which answers not found. Inserts are not checked. The stored
    ///     entity is read (raw, once per entity) only for an owned-only read grant, or for an AuditOnly read
    ///     policy that would hide the entity (<paramref name="auditTypes" /> collects those types; AuditOnly
    ///     never refuses). Returns true when at least one change is refused.
    /// </summary>
    private static async Task<bool> CheckVisibilityAsync(ICkCacheService ckCacheService,
        RtSecurityContext securityContext, string tenantId,
        Func<RtCkId<CkTypeId>, DataPermissionRules.TypeAccess> accessOf,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, StoredEntityCache storedEntities,
        OperationResult operationResult, List<NotVisibleChange>? notVisible,
        HashSet<RtCkId<CkTypeId>> auditTypes)
    {
        var targets = new List<NotVisibleChange>();
        foreach (var item in entityUpdateInfoList)
        {
            if (item.ModOption != EntityModOptions.Insert && item.RtId != null)
            {
                targets.Add(new NotVisibleChange(item.CkTypeId,item.RtId.Value,
                    item.ModOption == EntityModOptions.Delete ? RtDataAction.Delete : RtDataAction.Write));
            }
        }

        // An association whose origin is inserted by the same change set (the parent link of a new file or
        // folder) has no stored entity yet, so it can be neither visible nor hidden: the insert is the write
        // that is decided by the write policy, the association is its part and needs no visibility question.
        var insertedInThisChange = entityUpdateInfoList
            .Where(i => i.ModOption == EntityModOptions.Insert)
            // The CreateInsert(ckTypeId, entity) overload leaves RtId empty; the id is the entity's own.
            .Select(i => (RtId: i.RtId ?? i.RtEntity?.RtId, i.CkTypeId))
            .Where(i => i.RtId != null)
            .Select(i => new RtEntityId(i.CkTypeId, i.RtId!.Value))
            .ToHashSet();
        targets.AddRange(associationUpdateInfoList
            .Where(a => !insertedInThisChange.Contains(a.Origin))
            .Select(a => new NotVisibleChange(a.Origin.CkTypeId, a.Origin.RtId, RtDataAction.Write)));

        var refused = false;
        foreach (var typeGroup in targets.Distinct().GroupBy(t => t.CkTypeId))
        {
            var access = accessOf(typeGroup.Key);
            var auditHides = access.EnforcedRead is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed &&
                             access.AuditRead is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly;
            if (access.EnforcedRead is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed && !auditHides)
            {
                continue;
            }

            var ownerAttributePath = access.EnforcedRead == RtDataAccessLevel.Denied
                ? null
                : RtDataPermissionCkTypeHelper.GetEffectiveOwnerAttributePath(ckCacheService, tenantId,
                    typeGroup.Key);

            foreach (var change in typeGroup)
            {
                bool hidden;
                if (access.EnforcedRead == RtDataAccessLevel.Denied)
                {
                    // No read grant: the entity is invisible whether or not it exists, nothing to read.
                    hidden = true;
                }
                else
                {
                    var entity = await storedEntities.GetAsync(typeGroup.Key, change.RtId).ConfigureAwait(false);
                    if (access.EnforcedRead == RtDataAccessLevel.OwnedOnly)
                    {
                        // Like the evaluator: a foreign entity and a missing one are the same "not found".
                        hidden = entity == null || !DataPermissionRules.IsOwnedBy(entity, ckCacheService, tenantId,
                            ownerAttributePath, securityContext);
                    }
                    else
                    {
                        hidden = false;
                        if (entity != null && (access.AuditRead == RtDataAccessLevel.Denied ||
                                               !DataPermissionRules.IsOwnedBy(entity, ckCacheService, tenantId,
                                                   ownerAttributePath, securityContext)))
                        {
                            // AuditOnly read policy that would hide the entity: audited, never refused.
                            auditTypes.Add(typeGroup.Key);
                        }
                    }
                }

                if (!hidden)
                {
                    continue;
                }

                refused = true;
                notVisible?.Add(change);
                operationResult.AddMessage(new OperationMessage(MessageLevel.Error,
                    $"{change.CkTypeId}@{change.RtId}", NotVisibleMessageNumber,
                    $"Entity '{change.CkTypeId.SemanticVersionedFullName}@{change.RtId}' not found."));
            }
        }

        return refused;
    }

    /// <summary>
    ///     Raw stored entities of one guard run, read at most once each on the caller's session (deliberately
    ///     not the query path: the caller's read filter hides the entities the checks need to see).
    /// </summary>
    private sealed class StoredEntityCache(IRuntimeRepository runtimeRepository, IOctoSession session)
    {
        private readonly Dictionary<RtEntityId, RtEntity?> _entities = new();

        public async Task<RtEntity?> GetAsync(RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId)
        {
            var key = new RtEntityId(ckTypeId, rtId);
            if (!_entities.TryGetValue(key, out var entity))
            {
                entity = await runtimeRepository.GetRtEntityByRtIdAsync(session, key).ConfigureAwait(false);
                _entities[key] = entity;
            }

            return entity;
        }
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
        ICkCacheService ckCacheService, StoredEntityCache storedEntities, RtSecurityContext securityContext,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds, OperationResult operationResult)
    {
        // AB#4978: a CK-model-declared owner attribute replaces the stamped creator as the owner.
        var ownerAttributePath = RtDataPermissionCkTypeHelper.GetEffectiveOwnerAttributePath(ckCacheService,
            runtimeRepository.TenantId, ckTypeId);

        foreach (var rtId in rtIds)
        {
            // Deliberately the raw document read (not the query path): the caller's read filter hides
            // foreign owned-only entities, but the ownership check must see the stored owner.
            var entity = await storedEntities.GetAsync(ckTypeId, rtId).ConfigureAwait(false);
            if (entity == null)
            {
                // Ids without a stored entity are left to the normal write path (no-op semantics).
                continue;
            }

            // Rows without an owner value are not writable under an owned-only grant (fail closed).
            if (!DataPermissionRules.IsOwnedBy(entity, ckCacheService, runtimeRepository.TenantId,
                    ownerAttributePath, securityContext))
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
}
