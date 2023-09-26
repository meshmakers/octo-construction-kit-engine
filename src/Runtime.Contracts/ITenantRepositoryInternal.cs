using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace  Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Interface of runtime repository, a repository that is used to access runtime entities.
/// </summary>
public interface ITenantRepositoryInternal
{
    /// <summary>
    /// Returns the tenant id
    /// </summary>
    string TenantId { get; }
    
    /// <summary>
    /// Gets associations for a runtime entity.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">Object id of the runtime entity</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId, GraphDirections direction);
    
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
    
    /// <summary>
    /// Gets an entity by its runtime id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">The runtime id</param>
    /// <returns></returns>
    Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId);
}