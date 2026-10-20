using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     The single implementation of the entity-level data-permission rules (AB#7035) that
///     <see cref="DataPermissionWriteGuard" /> (repository write funnel) and
///     <see cref="RtDataPermissionEvaluator" /> (public check API) both use: type classification per
///     action (enforcement and audit view), the ownership predicate and the audit signal. The read filter
///     (<see cref="DataSecurityFilterFactory" />) classifies with the same
///     <see cref="RtDataAccessEvaluator" /> and renders the same ownership predicate into the query.
/// </summary>
internal static class DataPermissionRules
{
    internal static TypeAccess ClassifyType(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> ckTypeId, RtDataPolicyTable policyTable, RtSecurityContext securityContext)
    {
        var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId, ckTypeId);
        return ClassifyType(selfAndBase, policyTable, securityContext);
    }

    internal static TypeAccess ClassifyType(IReadOnlyCollection<string> selfAndBase, RtDataPolicyTable policyTable,
        RtSecurityContext securityContext)
    {
        return new TypeAccess(
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Write, securityContext, false),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Delete, securityContext, false),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Write, securityContext, true),
            RtDataAccessEvaluator.Classify(policyTable, selfAndBase, RtDataAction.Delete, securityContext, true),
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(policyTable, selfAndBase, securityContext));
    }

    /// <summary>
    ///     Ownership predicate for owned-only grants (AB#4978): the owner is the value of the CK-declared
    ///     owner attribute path, or the stamped creator when the type declares none. Entities without an
    ///     owner value are never owned (fail closed).
    /// </summary>
    internal static bool IsOwnedBy(RtEntity entity, ICkCacheService ckCacheService, string tenantId,
        string? ownerAttributePath, RtSecurityContext securityContext)
    {
        var owner = ownerAttributePath == null
            ? entity.RtCreatedBy
            : entity.GetAttributeValueByAccessPath(ckCacheService, tenantId, ownerAttributePath) as string;
        return owner != null && owner == securityContext.SubjectId;
    }

    internal static Task PublishWriteAuditAsync(IAuditEventSink? auditEventSink, string tenantId,
        RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId, RtDataAction action)
    {
        return auditEventSink?.PublishAsync(new AuditEvent(tenantId, AuditEventLevel.Warning,
                   "DataPermissions.WriteViolation",
                   $"Subject '{securityContext.SubjectId}' performed '{action}' on protected type " +
                   $"'{ckTypeId.SemanticVersionedFullName}' without a grant (AuditOnly policy — not blocked)."))
               ?? Task.CompletedTask;
    }

    internal static Task PublishReadAuditAsync(IAuditEventSink? auditEventSink, string tenantId,
        RtSecurityContext securityContext, RtCkId<CkTypeId> ckTypeId)
    {
        return auditEventSink?.PublishAsync(new AuditEvent(tenantId, AuditEventLevel.Warning,
                   "DataPermissions.ReadViolation",
                   $"Subject '{securityContext.SubjectId}' read protected type " +
                   $"'{ckTypeId.SemanticVersionedFullName}' without a grant (AuditOnly policy — not blocked)."))
               ?? Task.CompletedTask;
    }

    internal sealed record TypeAccess(
        RtDataAccessLevel EnforcedWrite,
        RtDataAccessLevel EnforcedDelete,
        RtDataAccessLevel AuditWrite,
        RtDataAccessLevel AuditDelete,
        RtBlueprintLockProtection BlueprintLock);
}
