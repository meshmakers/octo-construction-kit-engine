namespace Meshmakers.Octo.Runtime.Contracts.DataPermissions;

/// <summary>
///     Pure classification of a subject's access to a CK type under a policy table (AB#4972).
///     Semantics: a type is protected as soon as any considered rule targets it (directly or via a
///     base type the caller includes in <c>selfAndBaseCkTypeIds</c>); grants are additive; a
///     full-scope grant beats an owned-only grant; no matching grant on a protected type is denied.
///     System contexts always classify as <see cref="RtDataAccessLevel.Open" />.
/// </summary>
public static class RtDataAccessEvaluator
{
    /// <summary>
    ///     Classifies access of the given security context to a CK type for one action.
    /// </summary>
    /// <param name="table">The tenant's policy table</param>
    /// <param name="selfAndBaseCkTypeIds">
    ///     The full CK type id of the queried type plus all its base type ids (derived types inherit
    ///     policies targeting a base/collection-root type)
    /// </param>
    /// <param name="action">The action to classify</param>
    /// <param name="securityContext">The caller</param>
    /// <param name="includeAuditOnlyPolicies">
    ///     False = enforcement view (AuditOnly policies are ignored, their types stay open);
    ///     true = audit view (AuditOnly policies count as protecting — used to log would-be violations)
    /// </param>
    public static RtDataAccessLevel Classify(RtDataPolicyTable table,
        IReadOnlyCollection<string> selfAndBaseCkTypeIds, RtDataAction action,
        RtSecurityContext securityContext, bool includeAuditOnlyPolicies)
    {
        if (securityContext.IsSystem || !table.HasRules)
        {
            return RtDataAccessLevel.Open;
        }

        var protectedByAny = false;
        var hasFullGrant = false;
        var hasOwnedGrant = false;

        foreach (var rule in table.Rules)
        {
            if (rule.AuditOnly && !includeAuditOnlyPolicies)
            {
                continue;
            }

            if (!selfAndBaseCkTypeIds.Any(rule.TargetCkTypeIds.Contains))
            {
                continue;
            }

            protectedByAny = true;

            if (!rule.Actions.Contains(action))
            {
                continue;
            }

            if (!securityContext.Roles.Any(rule.GrantedRoleNames.Contains))
            {
                continue;
            }

            if (rule.OwnedOnly)
            {
                hasOwnedGrant = true;
            }
            else
            {
                hasFullGrant = true;
            }
        }

        if (!protectedByAny)
        {
            return RtDataAccessLevel.Open;
        }

        if (hasFullGrant)
        {
            return RtDataAccessLevel.Allowed;
        }

        if (hasOwnedGrant)
        {
            // An owned-only grant is unusable without a subject — fail closed.
            return securityContext.SubjectId is null ? RtDataAccessLevel.Denied : RtDataAccessLevel.OwnedOnly;
        }

        return RtDataAccessLevel.Denied;
    }
}
