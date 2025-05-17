using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Pair of rt association roles id and direction.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record NavigationPair
{
    /// <summary>
    /// Gets the navigation pair used to further traverse the object graph.
    /// </summary>
    public NavigationPair? InnerNavigationPair { get; internal set; }

    /// <summary>
    /// Gets the association role id.
    /// </summary>
    public CkId<CkAssociationRoleId> CkRoleId { get; }

    /// <summary>
    /// Gets the direction of the association.
    /// </summary>
    public GraphDirections Direction { get; }

    /// <summary>
    /// Gets the target construction kit type id.
    /// </summary>
    public CkId<CkTypeId> TargetCkTypeId { get; }

    /// <summary>
    ///     Creates a new <see cref="NavigationPair" /> from the given <paramref name="ckRoleId" />, <paramref name="direction" />, and <paramref name="targetCkTypeId" />.
    /// </summary>
    /// <param name="ckRoleId">Association role id</param>
    /// <param name="direction">Direction of the association</param>
    /// <param name="targetCkTypeId">Target construction kit type id</param>
    public NavigationPair(
        CkId<CkAssociationRoleId> ckRoleId,
        GraphDirections direction,
        CkId<CkTypeId> targetCkTypeId)
    {
        CkRoleId = ckRoleId;
        Direction = direction;
        TargetCkTypeId = targetCkTypeId;
        InnerNavigationPair = null;
    }

    /// <summary>
    ///     Creates a new <see cref="NavigationPair" /> from the given <paramref name="ckRoleId" />, <paramref name="direction" />, and <paramref name="targetCkTypeId" />.
    /// </summary>
    /// <param name="ckRoleId">Association role id</param>
    /// <param name="direction">Direction of the association</param>
    /// <param name="targetCkTypeId">Target construction kit type id</param>
    /// <param name="innerNavigationPair">Navigation pair used to further traverse the object graph</param>
    public NavigationPair(
        CkId<CkAssociationRoleId> ckRoleId,
        GraphDirections direction,
        CkId<CkTypeId> targetCkTypeId,
        NavigationPair? innerNavigationPair)
     : this(ckRoleId, direction, targetCkTypeId)
    {
        InnerNavigationPair = innerNavigationPair;
    }
}

