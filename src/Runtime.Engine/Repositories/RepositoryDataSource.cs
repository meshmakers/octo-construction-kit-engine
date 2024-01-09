using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Base class for a data source for a repository
/// </summary>
public abstract class RepositoryDataSource : IRepositoryDataSource
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    protected RepositoryDataSource(string tenantId)
    {
        TenantId = tenantId;
    }

    /// <inheritdoc />
    public string TenantId { get; }

    /// <inheritdoc />
    public abstract IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId)
        where TEntity : RtEntity, new();

    /// <inheritdoc />
    public IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>() where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        return GetRtCollection<TEntity>(ckTypeId);
    }

    /// <inheritdoc />
    public abstract IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction)
    {
        var associations = new List<RtAssociation>();
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        if (direction == GraphDirections.Any || direction == GraphDirections.Inbound)
        {
            associations.AddRange(queryable.Where(x =>
                x.TargetRtId == rtId));
        }

        if (direction == GraphDirections.Any || direction == GraphDirections.Outbound)
        {
            associations.AddRange(queryable.Where(x =>
                x.OriginRtId == rtId));
        }

        return associations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, IEnumerable<OctoObjectId> rtIds, GraphDirections direction)
    {
        var associations = new List<RtAssociation>();
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        if (direction == GraphDirections.Any || direction == GraphDirections.Inbound)
        {
            associations.AddRange(queryable.Where(x =>
                rtIds.Contains(x.TargetRtId)));
        }

        if (direction == GraphDirections.Any || direction == GraphDirections.Outbound)
        {
            associations.AddRange(queryable.Where(x =>
                rtIds.Contains(x.OriginRtId)));
        }

        return associations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction, CkId<CkAssociationRoleId> roleId)
    {
        var associations = new List<RtAssociation>();
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        if (direction == GraphDirections.Any || direction == GraphDirections.Inbound)
        {
            associations.AddRange(queryable.Where(x =>
                x.TargetRtId == rtId && x.AssociationRoleId == roleId));
        }

        if (direction == GraphDirections.Any || direction == GraphDirections.Outbound)
        {
            associations.AddRange(queryable.Where(x =>
                x.OriginRtId == rtId && x.AssociationRoleId == roleId));
        }

        return associations;
    }

    /// <inheritdoc />
    public abstract Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);

    /// <inheritdoc />
    public async Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId)
    {
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);
        return queryable
            .FirstOrDefault(a => a.OriginRtId == originRtEntityId.RtId && a.OriginCkTypeId == originRtEntityId.CkTypeId
                                                                       && a.TargetRtId == targetRtEntityId.RtId &&
                                                                       a.TargetCkTypeId == targetRtEntityId.CkTypeId
                                                                       && a.AssociationRoleId == ckRoleId);
    }

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId)
    {
        return new RtAssociation
        {
            AssociationId = OctoObjectId.GenerateNewId(),
            AssociationRoleId = ckRoleId,
            OriginCkTypeId = originRtEntityId.CkTypeId,
            OriginRtId = originRtEntityId.RtId,
            TargetCkTypeId = targetRtEntityId.CkTypeId,
            TargetRtId = targetRtEntityId.RtId
        };
    }
}