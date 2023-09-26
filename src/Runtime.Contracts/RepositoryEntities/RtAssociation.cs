using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents a runtime association, the instance of a connection between two runtime entities.
/// </summary>
public class RtAssociation
{
    /// <summary>
    /// Gets or sets the object id of the association
    /// </summary>
    public OctoObjectId AssociationId { get; set; }

    /// <summary>
    /// Gets or sets the object id of the origin runtime entity
    /// </summary>
    public OctoObjectId OriginRtId { get; set; }

    /// <summary>
    /// Gets or sets the origin ck type id.
    /// </summary>
    public CkId<CkTypeId> OriginCkTypeId { get; set; }

    /// <summary>
    /// Gets or sets the object id of the target runtime entity
    /// </summary>
    public OctoObjectId TargetRtId { get; set; }

    /// <summary>
    /// Gets or sets the target ck type id.
    /// </summary>
    public CkId<CkTypeId> TargetCkTypeId { get; set; }

    /// <summary>
    /// Gets or sets the association role id of the association role
    /// </summary>
    public CkId<CkAssociationRoleId> AssociationRoleId { get; set; }
}
