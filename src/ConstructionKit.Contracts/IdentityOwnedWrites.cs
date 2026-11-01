namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     AB#8042: the identity-owned fields of <c>System.Identity/User</c> and the identity associations that only the identity
///     service writes (its REST API and the identity CK methods, which enforce the role-change guard, the audit and the
///     self-service rules). The generic write paths refuse them for every caller: asset-repo GraphQL create, update and
///     query-row update, and the user-initiated (caller) path of <c>ImportRt</c>. The system import (blueprint seeds and
///     updates, CK migrations), the identity service's own repository writes and a tenant restore are not affected.
/// </summary>
/// <remarks>
///     <para>
///         <b>Interim, by decision (Gerald 2026-11-01, option B).</b> The right place for the attribute rule is the CK model
///         (<c>access: MethodOnly</c>), but none of these attributes was <c>securitySensitive</c> in any earlier 2.x version of
///         System.Identity, so tightening their access is a Major (row T7, AB#6585) and System.Identity 3.0.0 would leave every
///         blueprint range <c>[2.x,3.0)</c> unsatisfied. The CK model cannot restrict association writes at all (access exists
///         only on attributes). Move the attributes into the model's <c>access</c> with the next System.Identity major,
///         together with AB#7777 (<c>replacesGenericCreate</c> on <c>User</c>), and delete <see cref="UserAttributeNames" />
///         then; the association list stays until the CK access model can express association writes.
///     </para>
///     <para>
///         Matching is by version-less runtime id (model name and element id) and, for attributes, by the attribute name of the
///         type assignment, case-insensitive (GraphQL sends camelCase). <c>Claims</c> is the assignment name of
///         <c>System.Identity/UserClaims</c>.
///     </para>
/// </remarks>
public static class IdentityOwnedWrites
{
    /// <summary>The refusal reason the write paths report (GraphQL error extension, logs).</summary>
    public const string RefusalReason = "identity-owned";

    /// <summary>The user type of System.Identity (final, so no derived type can carry the fields).</summary>
    public static readonly RtCkId<CkTypeId> UserCkTypeId = new("System.Identity/User");

    /// <summary>
    ///     User attributes (assignment names) that only the identity service writes. Already restricted by the model and
    ///     therefore not listed: <c>UserName</c>, <c>Email</c> (MethodOnly), <c>PasswordHash</c>, <c>SecurityStamp</c>,
    ///     <c>UserTokens</c> (Hidden). <c>LockoutEnd</c> and <c>AccessFailedCount</c> are <c>ReadOnly</c> in the model, which
    ///     the generic write paths refuse but a user-started import accepts (the general rule: ReadOnly is importable); they are
    ///     listed so that such an import cannot reset a lockout (AB#8059, decision Gerald 2026-11-01).
    /// </summary>
    public static IReadOnlyCollection<string> UserAttributeNames => UserAttributeNameSet;

    private static readonly HashSet<string> UserAttributeNameSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "NormalizedUserName",
        "NormalizedEmail",
        "EmailConfirmed",
        "PhoneNumberConfirmed",
        "TwoFactorEnabled",
        "LockoutEnabled",
        "LockoutEnd",
        "AccessFailedCount",
        "ResetPasswordOnLogin",
        "Claims",
        "UserLogins",
        "PreferredChannelBindingId"
    };

    /// <summary>
    ///     Association roles whose edges only the identity service writes, in both directions (refused on whichever end the
    ///     write names): role assignment of users, groups and clients, group membership and nesting, client impersonation
    ///     and the role grants of data permissions. <c>PolicyPermission</c> and <c>IdentifiesUser</c> are not listed (open).
    /// </summary>
    public static IReadOnlyList<RtCkId<CkAssociationRoleId>> AssociationRoleIds { get; } =
    [
        new("System.Identity/AssignedRole"),
        new("System.Identity/GroupMember"),
        new("System.Identity/ChildGroup"),
        new("System.Identity/MayActAs"),
        new("System.Identity/GrantsPermission")
    ];

    private static readonly HashSet<string> AssociationRoleNames =
        new(AssociationRoleIds.Select(VersionLessName), StringComparer.OrdinalIgnoreCase);

    /// <summary><c>true</c> when the type is <c>System.Identity/User</c> (version-less comparison).</summary>
    public static bool IsUserType(RtCkId<CkTypeId>? ckTypeId) =>
        ckTypeId is { IsEmpty: false } &&
        string.Equals(VersionLessName(ckTypeId), VersionLessName(UserCkTypeId), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     <c>true</c> when <paramref name="attributeName" /> (the assignment name, any casing) of an entity of
    ///     <paramref name="ckTypeId" /> is identity-owned and must not be written by a generic path.
    /// </summary>
    public static bool IsIdentityOwnedAttribute(RtCkId<CkTypeId>? ckTypeId, string? attributeName) =>
        !string.IsNullOrEmpty(attributeName) && IsUserType(ckTypeId) && UserAttributeNameSet.Contains(attributeName!);

    /// <summary><c>true</c> when edges of this association role must not be written by a generic path.</summary>
    public static bool IsIdentityOwnedAssociation(RtCkId<CkAssociationRoleId>? associationRoleId) =>
        associationRoleId is { IsEmpty: false } && AssociationRoleNames.Contains(VersionLessName(associationRoleId));

    /// <summary>
    ///     <c>Model/Element</c> without a model version or major qualifier: an id read from a file or built from a versioned
    ///     string keeps the version in its model part (<c>System.Identity-2.30.0/User</c>, <c>System.Identity@2/User</c>).
    /// </summary>
    private static string VersionLessName<TElementId>(RtCkId<TElementId> id)
        where TElementId : IComparable<TElementId>, ICkElementId
    {
        var model = id.ModelId;
        var at = model.IndexOf('@');
        if (at >= 0)
        {
            model = model.Substring(0, at);
        }
        else
        {
            // "Name-1.2.3" (a model name may itself contain '-', so only a numeric version suffix is cut)
            var dash = model.LastIndexOf('-');
            if (dash > 0 && dash < model.Length - 1 && char.IsDigit(model[dash + 1]))
            {
                model = model.Substring(0, dash);
            }
        }

        return $"{model}/{id.ElementId.FullName}";
    }
}
