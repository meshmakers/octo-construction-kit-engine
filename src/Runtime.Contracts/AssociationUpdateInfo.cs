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
    [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include)]
    public AssociationModOptionsDto ModOption { get; }
    
    /// <summary>
    /// Creates an association update info for creating an association.
    /// </summary>
    /// <param name="origin">Runtime entity identifier of the origin entity.</param>
    /// <param name="target">Runtime entity identifier of the target entity.</param>
    /// <param name="roleId">Role identifier of the association.</param>
    /// <returns>Create association update info.</returns>
    public static AssociationUpdateInfo CreateCreate(RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> roleId)
    {
        return new AssociationUpdateInfo(origin, target, roleId, AssociationModOptionsDto.Create);
    }
    
    /// <summary>
    /// Creates an association update info for deleting an association.
    /// </summary>
    /// <param name="origin">Runtime entity identifier of the origin entity.</param>
    /// <param name="target">Runtime entity identifier of the target entity.</param>
    /// <param name="roleId">Role identifier of the association.</param>
    /// <returns>Create association update info.</returns>
    public static AssociationUpdateInfo CreateDelete(RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> roleId)
    {
        return new AssociationUpdateInfo(origin, target, roleId, AssociationModOptionsDto.Delete);
    }
}