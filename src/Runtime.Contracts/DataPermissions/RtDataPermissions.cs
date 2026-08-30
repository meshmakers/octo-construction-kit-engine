using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Contracts.DataPermissions;

/// <summary>
///     Actions a data policy can grant on a CK type (AB#4972).
/// </summary>
public enum RtDataAction
{
    /// <summary>Read entities of the target types.</summary>
    Read,

    /// <summary>Create or modify entities of the target types.</summary>
    Write,

    /// <summary>Delete entities of the target types.</summary>
    Delete
}

/// <summary>
///     Effective access of a subject to a CK type for one action.
/// </summary>
public enum RtDataAccessLevel
{
    /// <summary>No policy targets the type — unrestricted (backward-compatible default).</summary>
    Open,

    /// <summary>The type is protected and a grant allows full access.</summary>
    Allowed,

    /// <summary>The type is protected and a grant allows access to own entities only (RtCreatedBy).</summary>
    OwnedOnly,

    /// <summary>The type is protected and no grant matches.</summary>
    Denied
}

/// <summary>
///     Well-known CK ids of the data-permission model (System.Identity 2.12.0+).
/// </summary>
public static class RtDataPermissionTypes
{
    /// <summary>CK type id of the DataPermission entity.</summary>
    public const string DataPermissionCkTypeId = "System.Identity/DataPermission";

    /// <summary>CK type id of the DataPolicy entity.</summary>
    public const string DataPolicyCkTypeId = "System.Identity/DataPolicy";

    /// <summary>CK type id of the Role entity.</summary>
    public const string RoleCkTypeId = "System.Identity/Role";

    /// <summary>Association role id linking a DataPolicy to its DataPermission.</summary>
    public const string PolicyPermissionRoleId = "System.Identity/PolicyPermission";

    /// <summary>Association role id linking a Role to a granted DataPermission.</summary>
    public const string GrantsPermissionRoleId = "System.Identity/GrantsPermission";
}

/// <summary>
///     One resolved data policy: the CK types it protects, the actions/scope it grants and the role
///     names it is granted to (via its DataPermission's GrantsPermission edges).
/// </summary>
/// <param name="PermissionId">Dot-namespaced id of the permission the policy belongs to</param>
/// <param name="TargetCkTypeIds">Full CK type ids the policy targets; derived types inherit</param>
/// <param name="Actions">Actions the policy grants</param>
/// <param name="OwnedOnly">True when the grant is restricted to entities created by the caller</param>
/// <param name="AuditOnly">True when violations are only logged, nothing is filtered or rejected</param>
/// <param name="GrantedRoleNames">Role names the permission is granted to</param>
public sealed record RtDataPolicyRule(
    string PermissionId,
    IReadOnlyCollection<string> TargetCkTypeIds,
    IReadOnlyCollection<RtDataAction> Actions,
    bool OwnedOnly,
    bool AuditOnly,
    IReadOnlyCollection<string> GrantedRoleNames);

/// <summary>
///     The resolved data-policy table of a tenant. An empty table means no type is protected.
///     Rule targets are canonicalized on construction (element version elided when 1, matching the
///     stored ckTypeId form) so a target entered as "Basic/Employee-1" and the stored "Basic/Employee"
///     compare equal in every ordinal string comparison downstream.
/// </summary>
public sealed record RtDataPolicyTable
{
    /// <summary>
    ///     Creates a table from resolved rules, canonicalizing each rule's target CK type ids.
    /// </summary>
    /// <param name="rules">All resolved policy rules</param>
    public RtDataPolicyTable(IReadOnlyList<RtDataPolicyRule> rules)
    {
        Rules = rules.Select(r => r with
        {
            TargetCkTypeIds = new HashSet<string>(
                r.TargetCkTypeIds.Select(RtDataPermissionCkTypeHelper.CanonicalCkTypeId),
                StringComparer.Ordinal)
        }).ToList();
        AllTargetCkTypeIds = new HashSet<string>(Rules.SelectMany(r => r.TargetCkTypeIds),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     All resolved policy rules (targets canonicalized).
    /// </summary>
    public IReadOnlyList<RtDataPolicyRule> Rules { get; }

    /// <summary>
    ///     The empty table (no policies — everything open).
    /// </summary>
    public static readonly RtDataPolicyTable Empty = new([]);

    /// <summary>
    ///     True when at least one policy rule exists.
    /// </summary>
    public bool HasRules => Rules.Count > 0;

    /// <summary>
    ///     All CK type ids any rule targets (for cheap "is anything protected here" pre-checks).
    /// </summary>
    public IReadOnlyCollection<string> AllTargetCkTypeIds { get; }
}

/// <summary>
///     Resolves the per-tenant data-policy table from the System.Identity entities in the tenant
///     database (AB#4972). Implementations cache per tenant; <see cref="Invalidate" /> drops a
///     tenant's cached table (e.g. on identity-entity change).
/// </summary>
public interface IDataPermissionResolver
{
    /// <summary>
    ///     Gets the (cached) policy table of the repository's tenant. Returns
    ///     <see cref="RtDataPolicyTable.Empty" /> when the data-permission model is not installed.
    /// </summary>
    /// <param name="runtimeRepository">Repository of the tenant to resolve</param>
    Task<RtDataPolicyTable> GetPolicyTableAsync(IRuntimeRepository runtimeRepository);

    /// <summary>
    ///     Drops the cached table of a tenant so the next resolve reloads it.
    /// </summary>
    /// <param name="tenantId">The tenant id</param>
    void Invalidate(string tenantId);
}
