using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Represents a runtime association, the instance of a connection between two runtime entities.
/// </summary>
public class RtAssociation : RtTypeWithAttributes
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociation" />
    /// </summary>
    public RtAssociation()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociation" />
    /// </summary>
    /// <param name="associationRoleId">Construction kit association role id</param>
    /// <param name="associationId">Object id</param>
    /// <param name="attributes">List of attributes</param>
    public RtAssociation(RtCkId<CkAssociationRoleId> associationRoleId, OctoObjectId associationId,
        IReadOnlyDictionary<string, object?> attributes)
        : base(attributes)
    {
        AssociationRoleId = associationRoleId;
        AssociationId = associationId;
    }

    /// <summary>
    ///     Gets or sets the object id of the association
    /// </summary>
    public OctoObjectId AssociationId { get; set; }

    /// <summary>
    ///     Gets or sets the object id of the origin runtime entity
    /// </summary>
    public OctoObjectId OriginRtId { get; set; }

    /// <summary>
    ///     Gets or sets the origin ck type id.
    /// </summary>
    public RtCkId<CkTypeId> OriginCkTypeId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the object id of the target runtime entity
    /// </summary>
    public OctoObjectId TargetRtId { get; set; }

    /// <summary>
    ///     Gets or sets the target ck type id.
    /// </summary>
    public RtCkId<CkTypeId> TargetCkTypeId { get; set; } = null!;
    
    /// <summary>
    ///     Gets or sets a list of attributes of the target ck type id, that are referential integrity attributes
    /// </summary>
    public List<RtCkId<CkAttributeId>>? TargetCkAttributeIds { get; set; }

    /// <summary>
    ///     Gets or sets the association role id of the association role
    /// </summary>
    public RtCkId<CkAssociationRoleId>? AssociationRoleId { get; set; }

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{AssociationRoleId}@{AssociationId}";
    }
}