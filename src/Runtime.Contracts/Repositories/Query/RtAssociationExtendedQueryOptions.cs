using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents extended query options for runtime associations.
/// </summary>
public record RtAssociationExtendedQueryOptions : RtAssociationBaseQueryOptions
{
    /// <summary>
    /// Gets if defined a value indicating to filter by a specific role id
    /// </summary>
    public RtCkId<CkAssociationRoleId>? RoleId { get; }

    /// <summary>
    /// Gets if defined a value indicating to filter by a specific related type id
    /// </summary>
    public RtCkId<CkTypeId>? RelatedRtCkTypeId { get; }

    /// <summary>
    /// Defines if set a value indicating to filter by a specific related runtime entity id
    /// </summary>
    public OctoObjectId? RelatedRtId { get; }

    private RtAssociationExtendedQueryOptions(GraphDirections direction, RtCkId<CkAssociationRoleId>? roleId = null,
        RtCkId<CkTypeId>? relatedRtCkTypeId = null,
        OctoObjectId? relatedRtId = null, int? skip = null,
        int? take = null) : base(direction, skip, take)
    {
        RoleId = roleId;
        RelatedRtCkTypeId = relatedRtCkTypeId;
        RelatedRtId = relatedRtId;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociationBaseQueryOptions" />.
    /// </summary>
    /// <param name="direction">The graph direction for query</param>
    /// <returns></returns>
    public static RtAssociationExtendedQueryOptions Create(GraphDirections direction)
    {
        return new RtAssociationExtendedQueryOptions(direction);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociationBaseQueryOptions" />.
    /// </summary>
    /// <param name="direction">The graph direction for query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <returns></returns>
    public new static RtAssociationExtendedQueryOptions Create(GraphDirections direction, int? skip,
        int? take)
    {
        return new RtAssociationExtendedQueryOptions(direction, null, null, null, skip, take);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociationExtendedQueryOptions" />.
    /// </summary>
    /// <param name="direction">The graph direction for query</param>
    /// <param name="targetTypeId">Filter by target type id</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="roleId">Filter by role id</param>
    /// <param name="originTypeId">Filter by origin type id</param>
    /// <returns></returns>
    public static RtAssociationExtendedQueryOptions Create(GraphDirections direction,
        RtCkId<CkAssociationRoleId>? roleId,
        RtCkId<CkTypeId>? originTypeId = null, RtCkId<CkTypeId>? targetTypeId = null, int? skip = null,
        int? take = null)
    {
        return new RtAssociationExtendedQueryOptions(direction, roleId, targetTypeId, null, skip,
            take);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociationExtendedQueryOptions" />.
    /// </summary>
    /// <param name="direction">The graph direction for query</param>
    /// <param name="relatedRtCkTypeId">Filter by related type id</param>
    /// <param name="relatedRtId">Filter by related runtime entity id</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <param name="roleId">Filter by role id</param>
    /// <returns></returns>
    public static RtAssociationExtendedQueryOptions Create(GraphDirections direction,
        RtCkId<CkAssociationRoleId>? roleId, RtCkId<CkTypeId>? relatedRtCkTypeId,
        OctoObjectId? relatedRtId, int? skip = null,
        int? take = null)
    {
        return new RtAssociationExtendedQueryOptions(direction, roleId, relatedRtCkTypeId,
            relatedRtId, skip, take);
    }
}