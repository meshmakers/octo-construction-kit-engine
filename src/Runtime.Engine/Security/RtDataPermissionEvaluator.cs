using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     Public data-permission check (AB#7035), see <see cref="IRtDataPermissionEvaluator" />. Contains no
///     classification logic of its own: it composes <see cref="RtDataAccessEvaluator" /> (via
///     <see cref="DataPermissionRules" />), the shared ownership predicate and the blueprint-lock test of
///     <see cref="DataPermissionWriteGuard" />, i.e. the code the read filter and the write funnel use.
///     The stored entity is read (raw, on a system session) only when the verdict depends on it: an
///     owned-only grant on read or write, or a type that opted into blueprint-lock protection.
/// </summary>
internal sealed class RtDataPermissionEvaluator(
    IDataPermissionResolver dataPermissionResolver,
    ICkCacheService ckCacheService,
    IAuditEventSink? auditEventSink = null) : IRtDataPermissionEvaluator
{
    private static readonly RtDataPermissionVerdict OpenVerdict =
        new(RtDataPermissionDecision.Allowed, RtDataPermissionReason.Open);

    /// <inheritdoc />
    public async Task<RtDataPermissionVerdict> EvaluateAsync(IRuntimeRepository runtimeRepository,
        RtSecurityContext caller, RtEntityId target, RtDataAction action, bool publishAudit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeRepository);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();

        if (caller.IsSystem)
        {
            return OpenVerdict;
        }

        var table = await dataPermissionResolver.GetPolicyTableAsync(runtimeRepository).ConfigureAwait(false);
        if (!table.HasRules)
        {
            return OpenVerdict;
        }

        var tenantId = runtimeRepository.TenantId;
        var ckTypeId = target.CkTypeId;
        var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId, ckTypeId);

        // Visibility first: what the read filter hides does not exist for the caller, whatever the action.
        var read = RtDataAccessEvaluator.Classify(table, selfAndBase, RtDataAction.Read, caller, false);
        if (read == RtDataAccessLevel.Denied)
        {
            return new RtDataPermissionVerdict(RtDataPermissionDecision.NotVisible, RtDataPermissionReason.NoGrant);
        }

        var access = action == RtDataAction.Read
            ? null
            : DataPermissionRules.ClassifyType(selfAndBase, table, caller);
        var enforced = action switch
        {
            RtDataAction.Write => access!.EnforcedWrite,
            RtDataAction.Delete => access!.EnforcedDelete,
            _ => read
        };

        var needsEntity = read == RtDataAccessLevel.OwnedOnly || enforced == RtDataAccessLevel.OwnedOnly ||
                          (access != null && access.BlueprintLock != RtBlueprintLockProtection.None &&
                           enforced != RtDataAccessLevel.Denied);
        RtEntity? entity = null;
        if (needsEntity)
        {
            using var systemSession = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);
            entity = await runtimeRepository.GetRtEntityByRtIdAsync(systemSession, target).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (entity == null && (read == RtDataAccessLevel.OwnedOnly || enforced == RtDataAccessLevel.OwnedOnly))
            {
                return new RtDataPermissionVerdict(RtDataPermissionDecision.NotVisible,
                    RtDataPermissionReason.EntityNotFound);
            }
        }

        var ownerAttributePath = read == RtDataAccessLevel.OwnedOnly || enforced == RtDataAccessLevel.OwnedOnly
            ? RtDataPermissionCkTypeHelper.GetEffectiveOwnerAttributePath(ckCacheService, tenantId, ckTypeId)
            : null;
        var owned = entity != null && DataPermissionRules.IsOwnedBy(entity, ckCacheService, tenantId,
            ownerAttributePath, caller);

        if (read == RtDataAccessLevel.OwnedOnly && !owned)
        {
            return new RtDataPermissionVerdict(RtDataPermissionDecision.NotVisible, RtDataPermissionReason.NotOwner);
        }

        if (action == RtDataAction.Read)
        {
            var auditRead = read is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed &&
                            RtDataAccessEvaluator.Classify(table, selfAndBase, RtDataAction.Read, caller, true) is
                                RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly;
            if (auditRead && publishAudit)
            {
                await DataPermissionRules.PublishReadAuditAsync(auditEventSink, tenantId, caller, ckTypeId)
                    .ConfigureAwait(false);
            }

            return Allowed(read, auditRead);
        }

        switch (enforced)
        {
            case RtDataAccessLevel.Denied:
                return new RtDataPermissionVerdict(RtDataPermissionDecision.NotPermitted,
                    RtDataPermissionReason.NoGrant);
            case RtDataAccessLevel.OwnedOnly when !owned:
                return new RtDataPermissionVerdict(RtDataPermissionDecision.NotPermitted,
                    RtDataPermissionReason.NotOwner);
        }

        var audit = action == RtDataAction.Delete ? access!.AuditDelete : access!.AuditWrite;
        var auditViolation = enforced is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed &&
                             audit is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly;
        if (auditViolation && publishAudit)
        {
            await DataPermissionRules.PublishWriteAuditAsync(auditEventSink, tenantId, caller, ckTypeId, action)
                .ConfigureAwait(false);
        }

        if (access.BlueprintLock != RtBlueprintLockProtection.None && entity != null &&
            DataPermissionWriteGuard.IsBlueprintLocked(entity))
        {
            if (access.BlueprintLock == RtBlueprintLockProtection.Enforce)
            {
                return new RtDataPermissionVerdict(RtDataPermissionDecision.NotPermitted,
                    RtDataPermissionReason.BlueprintLocked);
            }

            if (publishAudit && auditEventSink != null)
            {
                await auditEventSink.PublishAsync(new AuditEvent(tenantId, AuditEventLevel.Warning,
                    "DataPermissions.BlueprintLockViolation",
                    $"Subject '{caller.SubjectId}' changed a blueprint-locked entity of " +
                    $"'{ckTypeId.SemanticVersionedFullName}' (AuditOnly policy — not blocked).")).ConfigureAwait(false);
            }

            auditViolation = true;
        }

        return Allowed(enforced, auditViolation);
    }

    /// <inheritdoc />
    public async Task<RtDataPermissionVerdict> EvaluateTypeAsync(IRuntimeRepository runtimeRepository,
        RtSecurityContext caller, RtCkId<CkTypeId> ckTypeId, RtDataAction action, bool publishAudit = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeRepository);
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();

        if (caller.IsSystem)
        {
            return OpenVerdict;
        }

        var table = await dataPermissionResolver.GetPolicyTableAsync(runtimeRepository).ConfigureAwait(false);
        if (!table.HasRules)
        {
            return OpenVerdict;
        }

        var tenantId = runtimeRepository.TenantId;
        var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId, ckTypeId);
        var enforced = RtDataAccessEvaluator.Classify(table, selfAndBase, action, caller, false);
        if (enforced == RtDataAccessLevel.Denied)
        {
            return new RtDataPermissionVerdict(
                action == RtDataAction.Read ? RtDataPermissionDecision.NotVisible : RtDataPermissionDecision.NotPermitted,
                RtDataPermissionReason.NoGrant);
        }

        var auditViolation = enforced is RtDataAccessLevel.Open or RtDataAccessLevel.Allowed &&
                             RtDataAccessEvaluator.Classify(table, selfAndBase, action, caller, true) is
                                 RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly;
        if (auditViolation && publishAudit)
        {
            await (action == RtDataAction.Read
                    ? DataPermissionRules.PublishReadAuditAsync(auditEventSink, tenantId, caller, ckTypeId)
                    : DataPermissionRules.PublishWriteAuditAsync(auditEventSink, tenantId, caller, ckTypeId, action))
                .ConfigureAwait(false);
        }

        return Allowed(enforced, auditViolation);
    }

    private static RtDataPermissionVerdict Allowed(RtDataAccessLevel level, bool auditViolation)
    {
        if (auditViolation)
        {
            return new RtDataPermissionVerdict(RtDataPermissionDecision.Allowed, RtDataPermissionReason.AuditOnly,
                true);
        }

        return new RtDataPermissionVerdict(RtDataPermissionDecision.Allowed, level switch
        {
            RtDataAccessLevel.Allowed => RtDataPermissionReason.Granted,
            RtDataAccessLevel.OwnedOnly => RtDataPermissionReason.GrantedAsOwner,
            _ => RtDataPermissionReason.Open
        });
    }
}
