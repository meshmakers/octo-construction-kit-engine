using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Data transfer object for association update.
/// </summary>
public record AssociationUpdateInfo
{
    /// <summary>
    ///     Creates a new instance of <see cref="AssociationUpdateInfo" />.
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="target"></param>
    /// <param name="roleId"></param>
    /// <param name="modOption"></param>
    public AssociationUpdateInfo(RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> roleId,
        AssociationModOptionsDto modOption)
    {
        Origin = origin;
        Target = target;
        RoleId = roleId;
        ModOption = modOption;
    }

    /// <summary>
    ///     Origin entity id.
    /// </summary>
    public RtEntityId Origin { get; }

    /// <summary>
    ///     Target entity id.
    /// </summary>
    public RtEntityId Target { get; }

    /// <summary>
    ///     Role of the association.
    /// </summary>
    public CkId<CkAssociationRoleId> RoleId { get; }

    /// <summary>
    ///     Mod option.
    /// </summary>
    public AssociationModOptionsDto ModOption { get; }
}