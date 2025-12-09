using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Interface of runtime repository, a repository that is used to access runtime entities.
/// </summary>
public interface IRuntimeRepository
{
    /// <summary>
    ///     Returns the tenant id
    /// </summary>
    string TenantId { get; }

    /// <summary>
    /// Loads the construction kit cache for the tenant based on the data in the repository.
    /// </summary>
    /// <param name="cacheService">Cache service to load the cache into</param>
    /// <returns></returns>
    Task LoadCacheForTenantAsync(ICkCacheService cacheService);

    #region Transaction Handling

    /// <summary>
    ///     Gets a new session
    /// </summary>
    /// <returns>The session object to handle a transaction</returns>
    Task<IOctoSession> GetSessionAsync();

    #endregion Transaction Handling

    #region Data query (simple)

    /// <summary>
    ///     Gets an entity by its runtime id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">The runtime id</param>
    /// <returns></returns>
    Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId);

    /// <summary>
    ///     Gets an entity by its runtime id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">The object id</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task<TEntity?> GetRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtIds">Object ids of the runtime entities</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <returns>Returns a result set of the given type</returns>
    Task<IResultSet<RtEntity>> GetRtEntitiesByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null);

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtIds">Object ids of the runtime entities</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns>Returns a result set of the given type</returns>
    Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session, IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <returns></returns>
    Task<IResultSet<RtEntity>> GetRtEntitiesByTypeAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        RtEntityQueryOptions rtEntityQueryOptions,
        int? skip = null, int? take = null);

    /// <summary>
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        RtEntityQueryOptions rtEntityQueryOptions,
        int? skip = null, int? take = null) where TEntity : RtEntity, new();

    /// <summary>
    /// Gets associations for a runtime entity
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">Runtime entity identifier to get associations for</param>
    /// <param name="associationExtendedQueryOptions">Options of the association query</param>
    /// <returns>Result set with available associations</returns>
    Task<IResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        RtEntityId rtEntityId, RtAssociationExtendedQueryOptions associationExtendedQueryOptions);

    /// <summary>
    ///     Gets associations for a runtime entity.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityIds">Runtime entity identifiers to get associations for</param>
    /// <param name="associationExtendedQueryOptions">Options of the association query</param>
    /// <returns>Result set with available associations</returns>
    Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, RtAssociationExtendedQueryOptions associationExtendedQueryOptions);

    /// <summary>
    ///     Gets an association by its origin, target and role id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <returns></returns>
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId,
        RtCkId<CkAssociationRoleId> ckRoleId);

    /// <summary>
    ///     Returns the data source access object for the given entity type
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    Task<IQueryable<TEntity>> AsQueryableAsync<TEntity>(IOctoSession? session = null)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Returns the data source access object for the given entity type
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <returns></returns>
    IQueryable<TEntity> AsQueryable<TEntity>(IOctoSession? session = null)
        where TEntity : RtEntity, new();

    /// <summary>
    /// Retrieves a graph of runtime entities based on the given type and role id direction pairs.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="ckTypeId"></param>
    /// <param name="rtEntityQueryOptions"></param>
    /// <param name="roleIdDirectionPairs"></param>
    /// <param name="skip"></param>
    /// <param name="take"></param>
    /// <returns></returns>
    Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByTypeAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        RtEntityQueryOptions rtEntityQueryOptions, ICollection<NavigationPair> roleIdDirectionPairs,
        int? skip = null, int? take = null);

    /// <summary>
    /// Retrieves a graph of runtime entities based on the given runtime identifier and role id direction pairs.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtIds">Object ids of the runtime entities</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="roleIdDirectionPairs">>Role id direction pairs that are loaded with this request</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <returns>Returns a result set of the given type</returns>
    Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, IEnumerable<NavigationPair> roleIdDirectionPairs, int? skip = null,
        int? take = null);

    #endregion Data query (simple)

    #region Transient data handling

    /// <summary>
    ///     Creates an instance of a runtime association
    /// </summary>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <returns>A transient version of a role, need to be stored.</returns>
    RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId);

    /// <summary>
    ///     Creates an instance of a runtime entity
    /// </summary>
    /// <param name="rtCkTypeId">The model version independent construction kit type id</param>
    /// <returns>Instance of the given construction kit type</returns>
    Task<RtEntity> CreateTransientRtEntityByRtCkIdAsync(RtCkId<CkTypeId> rtCkTypeId);

    /// <summary>
    /// Creates an instance of a runtime entity
    /// </summary>
    /// <param name="ckTypeId">The model version specific construction kit type id</param>
    /// <returns>Instance of the given construction kit type</returns>
    Task<RtEntity> CreateTransientRtEntityAsync(CkId<CkTypeId> ckTypeId);

    /// <summary>
    ///     Creates a typed version of a runtime entity
    /// </summary>
    /// <typeparam name="TEntity">Type derived from RtEntity</typeparam>
    /// <returns>Instance of the given construction kit type</returns>
    Task<TEntity> CreateTransientRtEntityAsync<TEntity>() where TEntity : RtEntity, new();

    #endregion

    #region Modification (simple)

    /// <summary>
    ///     Inserts a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntity">Object to insert</param>
    /// <returns></returns>
    Task InsertOneRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, RtEntity rtEntity);

    /// <summary>
    ///     Inserts a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntity">Object to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Inserts multiple runtime entities
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntities">Objects to insert</param>
    /// <returns></returns>
    Task InsertManyRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, ICollection<RtEntity> rtEntities);

    /// <summary>
    ///     Inserts multiple runtime entities
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntities">Objects to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task InsertManyRtEntityAsync<TEntity>(IOctoSession session, ICollection<TEntity> rtEntities)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task ReplaceOneRtEntityByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        RtEntity rtEntity);

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="entity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task ReplaceOneRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria, RtEntity entity);

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="entity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria, TEntity entity)
        where TEntity : RtEntity, new();


    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateOneRtEntityByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        RtEntity rtEntity);

    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateOneRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria, RtEntity rtEntity);

    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a multiple runtime entities. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <returns></returns>
    Task UpdateManyRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria, RtEntity rtEntity);

    /// <summary>
    ///     Updates multiple runtime entities. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        TEntity rtEntity)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes a single runtime entity by its runtime id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime identifier of the entity</param>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <returns></returns>
    Task DeleteOneRtEntityByRtIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        DeleteOptions deleteOptions);

    /// <summary>
    ///     Deletes a single runtime entity by its runtime id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task DeleteOneRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId,
        DeleteOptions deleteOptions)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <returns></returns>
    Task DeleteOneRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions);

    /// <summary>
    ///     Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <returns></returns>
    Task DeleteManyRtEntitiesAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions);

    /// <summary>
    ///     Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <param name="deleteOptions">Option to control the delete operation</param>
    /// <returns></returns>
    Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions)
        where TEntity : RtEntity, new();

    #endregion Modification (simple)

    #region Modification (bulk)

    /// <summary>
    ///     Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="associationUpdateInfoList">List of runtime association updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult);

    /// <summary>
    ///     Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="associationUpdateInfoList">List of runtime association updates</param>
    /// <param name="deleteOptions">The default delete operation when there are entities to delete</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        DeleteOptions deleteOptions,
        OperationResult operationResult);

    /// <summary>
    ///     Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="associationUpdateInfoList">List of runtime association updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult);

    /// <summary>
    ///     Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult);

    /// <summary>
    ///     Applies changes to the runtime repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="entityUpdateInfoList">List of runtime entity updates</param>
    /// <param name="deleteOptions">The default delete operation when there are entities to delete</param>
    /// <param name="operationResult">Result of the operation</param>
    /// <returns></returns>
    Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        DeleteOptions deleteOptions,
        OperationResult operationResult);

    #endregion Modification (bulk)

    #region Large Binaries

    /// <summary>
    /// Uploads a file to be cached in the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="expiryDateTime">Expiry date time of the file</param>
    /// <param name="stream">Binary stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task<OctoObjectId> UploadTemporaryLargeBinaryAsync(IOctoSession session, string filename, string contentType,
        DateTime expiryDateTime, Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a cached large binary file in the repository based on the file name
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Object id of the large binary</returns>
    Task<OctoObjectId> ReplaceTemporaryLargeBinaryAsync(IOctoSession session, string filename, string contentType,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a large binary file from the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all expired temporary large binaries from the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="expiryDateTime">Expiry date time to filter expired binaries</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all temporary large binaries from the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Handler for the download stream</returns>
    Task<IDownloadStreamHandler> DownloadLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="binaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId binaryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a large binary file from the repository based on the filename and binary type
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fileName">Filename of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetTemporaryLargeBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default);

    #endregion Large Binaries

    #region Advanced functionality

    /// <summary>
    /// Imports a list of runtime entities in bulk.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntityList">List of runtime entities to import</param>
    /// <param name="options">Bulk operation options for the import</param>
    /// <returns>Aggregated result of the bulk import operation</returns>
    Task<AggregatedBulkImportResult>
        BulkInsertRtEntitiesAsync(IOctoSession session, IEnumerable<RtEntity> rtEntityList,
            BulkOperationOptions options);

    /// <summary>
    /// Imports a list of runtime entities in bulk.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtAssociations">List of runtime associations to import</param>
    /// <param name="options">Bulk operation options for the import</param>
    /// <returns>Aggregated result of the bulk import operation</returns>
    Task<IBulkImportResult> BulkRtAssociationsAsync(IOctoSession session, IEnumerable<RtAssociation> rtAssociations,
        BulkOperationOptions options);

    #endregion Advanced functionality

    /// <summary>
    /// Gets the construction kit type graph from the cache service
    /// </summary>
    /// <param name="ckTypeId">The ck type id</param>
    /// <returns></returns>
    /// <exception cref="RuntimeRepositoryException">CkTypeId does not exist in cache</exception>
    Task<CkTypeGraph> GetCkTypeGraphAsync(RtCkId<CkTypeId> ckTypeId);
}