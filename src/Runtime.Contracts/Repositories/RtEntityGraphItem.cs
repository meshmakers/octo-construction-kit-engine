using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

[DebuggerDisplay("{AssociationRoleId} -> {AssociationId}")]
public class NavigationEnd : RtTypeWithAttributes
{
    /// <summary>
    /// Gets or sets the navigation property name of the association
    /// </summary>
    public required string NavigationPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the target construction kit type id
    /// </summary>
    public required CkId<CkTypeId> TargetCkTypeId { get; set; }

    /// <summary>
    ///     Gets or sets the association role id of the association role
    /// </summary>
    public required CkId<CkAssociationRoleId> AssociationRoleId { get; set; }

    /// <summary>
    ///     Gets or sets the object id of the association
    /// </summary>
    public required OctoObjectId AssociationId { get; set; }

    /// <summary>
    /// Gets or sets the target construction kit type id
    /// </summary>
    public required IEnumerable<RtEntityGraphItem> Targets { get; set; }

    protected override string GetLocation()
    {
        return $"{nameof(NavigationEnd)}: {AssociationRoleId} -> {AssociationId}";
    }
}

public class RtEntityGraphItem : RtEntity
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityGraphItem" />
    /// </summary>
    public RtEntityGraphItem()
    {
        Associations = new List<NavigationEnd>();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityGraphItem" />
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Object id</param>
    public RtEntityGraphItem(CkId<CkTypeId> ckTypeId, OctoObjectId rtId)
        : base(ckTypeId, rtId)
    {
        Associations = new List<NavigationEnd>();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityGraphItem" />
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Object id</param>
    /// <param name="attributes">List of attributes</param>
    public RtEntityGraphItem(CkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        IReadOnlyDictionary<string, object?> attributes)
        : base(ckTypeId, rtId, attributes)
    {
        Associations = new List<NavigationEnd>();
    }

    /// <summary>
    /// Creates a new instance of <see cref="RtEntityGraphItem" />
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Object id</param>
    /// <param name="attributes">List of attributes</param>
    /// <param name="associations">List of associations</param>
    public RtEntityGraphItem(CkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        IReadOnlyDictionary<string, object?> attributes, List<NavigationEnd> associations)
        : base(ckTypeId, rtId, attributes)
    {
        Associations = associations;
    }

    /// <summary>
    /// Gets or sets the associations of the entity
    /// </summary>
    public List<NavigationEnd> Associations { get; set; }
}