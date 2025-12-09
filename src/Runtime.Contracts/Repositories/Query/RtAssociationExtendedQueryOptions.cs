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
    /// Gets if defined a value indicating to filter by a specific origin type id
    /// </summary>
    public RtCkId<CkTypeId>? OriginTypeId { get; }

    /// <summary>
    /// Gets if defined a value indicating to filter by a specific target type id
    /// </summary>
    public RtCkId<CkTypeId>? TargetTypeId { get; }

    internal RtAssociationExtendedQueryOptions(GraphDirections direction, RtCkId<CkAssociationRoleId>? roleId = null,
        RtCkId<CkTypeId>? originTypeId = null, RtCkId<CkTypeId>? targetTypeId = null, int? skip = null,
        int? take = null) : base(direction, skip, take)
    {
        RoleId = roleId;
        OriginTypeId = originTypeId;
        TargetTypeId = targetTypeId;
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
        RtCkId<CkAssociationRoleId>? roleId = null,
        RtCkId<CkTypeId>? originTypeId = null, RtCkId<CkTypeId>? targetTypeId = null, int? skip = null,
        int? take = null)
    {
        return new RtAssociationExtendedQueryOptions(direction, roleId, originTypeId, targetTypeId, skip, take);
    }
}