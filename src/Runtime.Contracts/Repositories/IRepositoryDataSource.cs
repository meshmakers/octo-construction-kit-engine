using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface for the data source of a runtime repository
/// </summary>
public interface IRepositoryDataSource
{
    /// <summary>
    /// Returns the corresponding tenant id
    /// </summary>
    public string TenantId { get; }
    
    /// <summary>
    /// Returns the data source access object for the given entity type
    /// </summary>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <typeparam name="TEntity">The type of entity derived from &lt;see cref="RtEntity"/&gt;</typeparam>
    /// <returns></returns>
    IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId) where TEntity : RtEntity, new();
    
    /// <summary>
    /// Returns the data source access object for the given entity type
    /// </summary>
    /// <typeparam name="TEntity">The type of entity derived from &lt;see cref="RtEntity"/&gt;</typeparam>
    /// <returns></returns>
    IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>() where TEntity : RtEntity, new();
    
    /// <summary>
    /// Returns the associations collection 
    /// </summary>
    IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }
    
    /// <summary>
    /// Gets associations for a runtime entity.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">Object id of the runtime entity</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId, GraphDirections direction);

    /// <summary>
    /// Gets associations for a runtime entity of a specific role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">Object id of the runtime entity</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <param name="roleId">The construction kit role to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction, CkId<CkAssociationRoleId> roleId);
    
    /// <summary>
    /// Returns the current multiplicity of a runtime association, that means the number of associations that exist for a give runtime entity and role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">Object id of the runtime entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId, CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);
    
    /// <summary>
    /// Gets an association by its origin, target and role id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <returns></returns>
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId, RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId);
    
    /// <summary>
    /// Creates an instance of a runtime association
    /// </summary>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <returns>A transient version of a role, need to be stored.</returns>
    RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId, RtEntityId targetRtEntityId);
}