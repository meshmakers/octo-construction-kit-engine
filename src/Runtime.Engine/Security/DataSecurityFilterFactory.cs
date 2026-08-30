using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     Builds the caller-specific, tenant-global read filter from the resolved policy table (AB#4973).
/// </summary>
internal class DataSecurityFilterFactory(
    IDataPermissionResolver dataPermissionResolver,
    ICkCacheService ckCacheService) : IDataSecurityFilterFactory
{
    /// <inheritdoc />
    public async Task<RtDataSecurityQueryFilter?> CreateAsync(IRuntimeRepository runtimeRepository,
        RtSecurityContext securityContext)
    {
        if (securityContext.IsSystem)
        {
            return null;
        }

        var table = await dataPermissionResolver.GetPolicyTableAsync(runtimeRepository).ConfigureAwait(false);
        if (!table.HasRules)
        {
            return null;
        }

        var tenantId = runtimeRepository.TenantId;

        // Expand every rule target to its concrete derived types once.
        var enforcedConcrete = new HashSet<string>(StringComparer.Ordinal);
        var auditConcrete = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in table.Rules)
        {
            var bucket = rule.AuditOnly ? auditConcrete : enforcedConcrete;
            foreach (var target in rule.TargetCkTypeIds)
            {
                foreach (var concrete in ExpandToConcreteTypeIds(tenantId, target))
                {
                    bucket.Add(concrete);
                }
            }
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var ownedOnly = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concrete in enforcedConcrete)
        {
            var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId,
                new RtCkId<CkTypeId>(concrete));
            var level = RtDataAccessEvaluator.Classify(table, selfAndBase, RtDataAction.Read, securityContext,
                includeAuditOnlyPolicies: false);
            switch (level)
            {
                case RtDataAccessLevel.Allowed:
                    allowed.Add(concrete);
                    break;
                case RtDataAccessLevel.OwnedOnly:
                    ownedOnly.Add(concrete);
                    break;
            }
        }

        // Types where only audit policies would restrict the caller: access stays open, reads get logged.
        var auditDenied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concrete in auditConcrete)
        {
            if (enforcedConcrete.Contains(concrete))
            {
                continue;
            }

            var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(ckCacheService, tenantId,
                new RtCkId<CkTypeId>(concrete));
            var auditLevel = RtDataAccessEvaluator.Classify(table, selfAndBase, RtDataAction.Read, securityContext,
                includeAuditOnlyPolicies: true);
            if (auditLevel is RtDataAccessLevel.Denied or RtDataAccessLevel.OwnedOnly)
            {
                auditDenied.Add(concrete);
            }
        }

        if (enforcedConcrete.Count == 0 && auditDenied.Count == 0)
        {
            return null;
        }

        // AB#4978: owned-only types whose CK type declares (or inherits) an owner attribute compare
        // that attribute's value against the subject instead of the stamped rtCreatedBy.
        Dictionary<string, string>? ownerAttributes = null;
        foreach (var concrete in ownedOnly)
        {
            var ownerAttributePath = RtDataPermissionCkTypeHelper.GetEffectiveOwnerAttributePath(ckCacheService,
                tenantId, new RtCkId<CkTypeId>(concrete));
            if (ownerAttributePath != null)
            {
                (ownerAttributes ??= new Dictionary<string, string>(StringComparer.Ordinal))[concrete] =
                    ownerAttributePath;
            }
        }

        return new RtDataSecurityQueryFilter(enforcedConcrete, allowed, ownedOnly, securityContext.SubjectId,
            auditDenied, ownerAttributes);
    }

    private IReadOnlyCollection<string> ExpandToConcreteTypeIds(string tenantId, string targetCkTypeId)
    {
        var rtCkTypeId = new RtCkId<CkTypeId>(targetCkTypeId);
        if (!ckCacheService.TryGetRtCkType(tenantId, rtCkTypeId, out var graph) || graph == null)
        {
            // Unknown target (e.g. model not installed): keep the literal id so the predicate still
            // matches rows of that type verbatim — fail closed rather than silently open.
            return [targetCkTypeId];
        }

        return new HashSet<string>(graph.GetAllDerivedTypes(true).Select(t => t.ToRtCkId().SemanticVersionedFullName),
            StringComparer.Ordinal);
    }
}
