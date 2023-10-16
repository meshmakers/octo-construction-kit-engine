using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface of runtime repository, a repository that is used to access runtime entities.
/// </summary>
public interface IRuntimeRepository
{
    /// <summary>
    /// Returns the tenant id
    /// </summary>
    string TenantId { get; }

    #region Transaction Handling

    /// <summary>
    /// Gets a new session
    /// </summary>
    /// <returns>The session object to handle a transaction</returns>
    Task<IOctoSession> GetSessionAsync();

    #endregion Transaction Handling

    #region Data query (simple)

    /// <summary>
    /// Gets an entity by its runtime id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">The runtime id</param>
    /// <returns></returns>
    Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId);

    /// <summary>
    /// Gets an entity by its runtime id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">The object id</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task<TEntity?> GetRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new();
    
    /// <summary>
    /// Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="dataQueryOperation">Query options for data query</param>
    /// <param name="skip">Amount of items to skip</param>
    /// <param name="take">Amount of items to take</param>
    /// <returns></returns>
    Task<IResultSet<RtEntity>> GetRtEntitiesByTypeAsync(IOctoSession session, CkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="dataQueryOperation">Query options for data query</param>
    /// <param name="skip">Amount of items to skip</param>
    /// <param name="take">Amount of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new();

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
    Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);

    /// <summary>
    /// Gets an association by its origin, target and role id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <returns></returns>
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId, RtEntityId targetRtEntityId,
        CkId<CkAssociationRoleId> ckRoleId);

    #endregion Data query (simple)

    #region Transient data handling

    /// <summary>
    /// Creates an instance of a runtime association
    /// </summary>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <returns>A transient version of a role, need to be stored.</returns>
    RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId);

    /// <summary>
    /// Creates an instance of a runtime entity
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <returns>Instance of the given construction kit type</returns>
    RtEntity CreateTransientRtEntity(CkId<CkTypeId> ckTypeId);

    /// <summary>
    /// Creates a typed version of a runtime entity
    /// </summary>
    /// <typeparam name="TEntity">Type derived from RtEntity</typeparam>
    /// <returns>Instance of the given construction kit type</returns>
    TEntity CreateTransientRtEntity<TEntity>() where TEntity : RtEntity, new();

    #endregion

    #region Modification (simple)

    /// <summary>
    /// Inserts a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntity">Object to insert</param>
    /// <returns></returns>
    Task InsertOneRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, RtEntity rtEntity);

    /// <summary>
    /// Inserts a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntity">Object to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    /// Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task ReplaceOneRtEntityByIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId, RtEntity rtEntity);

    /// <summary>
    /// Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    /// Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="entity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task ReplaceOneRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity entity);

    /// <summary>
    /// Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="entity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity entity)
        where TEntity : RtEntity, new();
    
    
    /// <summary>
    /// Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateOneRtEntityByIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId, RtEntity rtEntity);

    /// <summary>
    /// Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    /// Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateOneRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity rtEntity);
    
    /// <summary>
    /// Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
        where TEntity : RtEntity, new();
    
    /// <summary>
    /// Updates a multiple runtime entities. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateManyRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity rtEntity);
    
    /// <summary>
    /// Updates multiple runtime entities. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
        where TEntity : RtEntity, new();
    
    /// <summary>
    /// Deletes a single runtime entity by its runtime id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId"></param>
    /// <returns></returns>
    Task DeleteOneRtEntityByRtIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId);

    /// <summary>
    /// Deletes a single runtime entity by its runtime id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task DeleteOneRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new();

    /// <summary>
    /// Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <returns></returns>
    Task DeleteOneRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, ICollection<FieldFilter> fieldFilters);

    /// <summary>
    /// Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters)
        where TEntity : RtEntity, new();

    /// <summary>
    /// Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <returns></returns>
    Task DeleteManyRtEntitiesAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, ICollection<FieldFilter> fieldFilters);

    /// <summary>
    /// Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    /// <returns></returns>
    Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters)
        where TEntity : RtEntity, new();

    #endregion Modification (simple)

    #region Modification (bulk)

    /// <summary>
    /// Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="associationUpdateInfoList">List of runtime association updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChanges(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult);

    /// <summary>
    /// Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="associationUpdateInfoList">List of runtime association updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChanges(IOctoSession session, IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult);

    /// <summary>
    /// Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChanges(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList, OperationResult operationResult);

    #endregion Modification (bulk)
}