namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     The identity associations that only the identity service writes (its REST API and the identity CK methods, which enforce
///     the role-change guard, the audit and the self-service rules): role assignment of users, groups and clients, group
///     membership and nesting, client impersonation and the role grants of data permissions. The generic write paths refuse
///     them for every caller: asset-repo GraphQL create, update and query-row update, the mesh-adapter write nodes and the
///     user-initiated (caller) path of <c>ImportRt</c>. The system import (blueprint seeds and updates, CK migrations), the
///     identity service's own repository writes and a tenant restore are not affected.
/// </summary>
/// <remarks>
///     <para>
///         <b>Associations only (AB#8053).</b> AB#8042 listed the identity-owned attributes of <c>System.Identity/User</c> here as
///         well. Since System.Identity 3.0.0 the model says it itself: those attributes are <c>access: MethodOnly</c> in
///         <c>ck-user.yaml</c> and the generic access guards (GraphQL, <c>ImportRt</c> rule 139, the adapter) enforce it, so the
///         attribute list is gone. The association list stays, with a reason: the CK access model has no association-level write
///         restriction (<c>access</c> exists only on attributes, including the attributes of an association role, and an
///         association cannot be declared method-only). Remove the list when the CK language can express it.
///     </para>
///     <para>
///         Matching is by version-less runtime id (model name and element id).
///     </para>
/// </remarks>
public static class IdentityOwnedWrites
{
    /// <summary>The refusal reason the write paths report (GraphQL error extension, logs).</summary>
    public const string RefusalReason = "identity-owned";

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
