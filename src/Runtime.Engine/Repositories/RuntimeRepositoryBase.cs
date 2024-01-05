using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

// ReSharper disable MemberCanBePrivate.Global

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Represents a basic implementation of <see cref="IRuntimeRepository" />
/// </summary>
public abstract class RuntimeRepositoryBase : IRuntimeRepository
{
    private readonly ICkCacheService _ckCacheService;

    /// <summary>
    ///     Creates a new instance of <see cref="RuntimeRepositoryBase" />
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="ckCacheService">Construction kit cache service</param>
    /// <param name="repositoryDataSource">The corresponding repository data source</param>
    /// <param name="bulkRtMutation"></param>
    protected RuntimeRepositoryBase(string tenantId, ICkCacheService ckCacheService, IRepositoryDataSource repositoryDataSource,
        IBulkRtMutation bulkRtMutation)
    {
        BulkRtMutation = bulkRtMutation;
        RepositoryDataSource = repositoryDataSource;
        TenantId = tenantId;
        _ckCacheService = ckCacheService;
    }

    /// <summary>
    ///     The bulk mutation implementation
    /// </summary>
    protected IBulkRtMutation BulkRtMutation { get; }

    /// <summary>
    ///     Returns the data source of the repository
    /// </summary>
    protected IRepositoryDataSource RepositoryDataSource { get; }

    /// <inheritdoc />
    public string TenantId { get; }


    /// <inheritdoc />
    public abstract Task<IOctoSession> GetSessionAsync();

    /// <inheritdoc />
    public virtual async Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<RtEntity>(rtEntityId.CkTypeId);

        return await rtCollection.DocumentAsync(session, rtEntityId.RtId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);

        return await rtCollection.DocumentAsync(session, rtId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<RtEntity>> GetRtEntitiesByIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds, DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null)
    {
        return await GetRtEntitiesByIdAsync<RtEntity>(session, ckTypeId, rtIds, dataQueryOperation, skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session, IReadOnlyList<OctoObjectId> rtIds,
        DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        return await GetRtEntitiesByIdAsync<TEntity>(session, ckTypeId, rtIds, dataQueryOperation, skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<RtEntity>> GetRtEntitiesByTypeAsync(IOctoSession session, CkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation, int? skip = null,
        int? take = null)
    {
        return await GetRtEntitiesByTypeAsync<RtEntity>(session, ckTypeId, dataQueryOperation, skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session, DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        return await GetRtEntitiesByTypeAsync<TEntity>(session, ckTypeId, dataQueryOperation, skip, take).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction)
    {
        return await RepositoryDataSource.GetRtAssociationsAsync(session, rtId, direction).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction, CkId<CkAssociationRoleId> roleId)
    {
        return await RepositoryDataSource.GetRtAssociationsAsync(session, rtId, direction, roleId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        return await RepositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(session, rtEntityId, ckRoleId, direction)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId)
    {
        return await RepositoryDataSource.GetRtAssociationOrDefaultAsync(session, originRtEntityId, targetRtEntityId, ckRoleId)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IQueryable<TEntity>> AsQueryableAsync<TEntity>(IOctoSession? session = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);

        return await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IQueryable<TEntity> AsQueryable<TEntity>(IOctoSession? session = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);

        return rtCollection.AsQueryable(session);
    }

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId)
    {
        return RepositoryDataSource.CreateTransientRtAssociation(originRtEntityId, ckRoleId, targetRtEntityId);
    }

    /// <inheritdoc />
    public async Task<RtEntity> CreateTransientRtEntityAsync(CkId<CkTypeId> ckTypeId)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var ckTypeGraph = cacheService.GetCkType(TenantId, ckTypeId);
        return CreateTransientRtEntity<RtEntity>(ckTypeGraph);
    }

    /// <inheritdoc />
    public async Task<TEntity> CreateTransientRtEntityAsync<TEntity>() where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        if (string.IsNullOrWhiteSpace(ckTypeId.FullName))
        {
            throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(TEntity));
        }

        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var ckTypeGraph = cacheService.GetCkType(TenantId, ckTypeId);
        if (ckTypeGraph == null)
        {
            throw RuntimeRepositoryException.CkTypeIdDoesNotExistInCache(ckTypeId);
        }

        return CreateTransientRtEntity<TEntity>(ckTypeGraph);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, RtEntity rtEntity)
    {
        await InsertOneRtEntityAsync<RtEntity>(session, rtEntity.GetCkTypeId(), rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity) where TEntity : RtEntity, new()
    {
        await InsertOneRtEntityAsync(session, rtEntity.GetCkTypeId(), rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InsertManyRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, ICollection<RtEntity> rtEntities)
    {
        await InsertManyRtEntityAsync<RtEntity>(session, ckTypeId, rtEntities).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InsertManyRtEntityAsync<TEntity>(IOctoSession session, ICollection<TEntity> rtEntities)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        await InsertManyRtEntityAsync(session, ckTypeId, rtEntities).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityByIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId, RtEntity rtEntity)
    {
        await ReplaceOneRtEntityByIdAsync<RtEntity>(session, rtEntity.GetCkTypeId(), rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        await ReplaceOneRtEntityByIdAsync(session, rtEntity.GetCkTypeId(), rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity rtEntity)
    {
        await ReplaceOneRtEntityAsync(session, rtEntity.CkTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        await ReplaceOneRtEntityAsync(session, ckTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task UpdateOneRtEntityByIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId, RtEntity rtEntity)
    {
        await UpdateOneRtEntityByIdAsync<RtEntity>(session, ckTypeId, rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        await UpdateOneRtEntityByIdAsync(session, ckTypeId, rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity rtEntity)
    {
        await UpdateOneRtEntityAsync(session, rtEntity.CkTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        await UpdateOneRtEntityAsync(session, ckTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateManyRtEntityAsync(IOctoSession session, ICollection<FieldFilter> fieldFilters, RtEntity rtEntity)
    {
        await UpdateManyRtEntityAsync(session, rtEntity.CkTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        await UpdateManyRtEntityAsync(session, ckTypeId, fieldFilters, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityByRtIdAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        var entitiesUpdate = new[] { EntityUpdateInfo<RtEntity>.CreateDelete(new RtEntityId(ckTypeId, rtId)) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateDelete(new RtEntityId(ckTypeId, rtId)) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, ICollection<FieldFilter> fieldFilters)
    {
        await DeleteOneRtEntityAsync<RtEntity>(session, ckTypeId, fieldFilters).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        await DeleteOneRtEntityAsync<TEntity>(session, ckTypeId, fieldFilters).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteManyRtEntitiesAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, ICollection<FieldFilter> fieldFilters)
    {
        await DeleteManyRtEntitiesAsync<RtEntity>(session, ckTypeId, fieldFilters).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, ICollection<FieldFilter> fieldFilters)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();

        await DeleteManyRtEntitiesAsync<TEntity>(session, ckTypeId, fieldFilters).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entityUpdateInfoList, associationUpdateInfoList)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        await ApplyChangesAsync(session, new List<IEntityUpdateInfo<RtEntity>>(), associationUpdateInfoList, operationResult)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        await ApplyChangesAsync(session, entityUpdateInfoList, new List<AssociationUpdateInfo>(), operationResult).ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns the cache service that is used to access the construction kit model
    /// </summary>
    protected async Task<ICkCacheService> GetCkCacheServiceAsync()
    {
        if (!_ckCacheService.IsTenantLoaded(TenantId))
        {
            await RefreshCkCacheServiceAsync(_ckCacheService).ConfigureAwait(false);
        }

        return _ckCacheService;
    }

    /// <summary>
    ///     Refresh the cache service
    /// </summary>
    /// <param name="ckCacheService"></param>
    /// <returns></returns>
    protected abstract Task RefreshCkCacheServiceAsync(ICkCacheService ckCacheService);

    private TEntity CreateTransientRtEntity<TEntity>(CkTypeGraph ckTypeGraph)
        where TEntity : RtEntity, new()
    {
        if (ckTypeGraph.IsAbstract)
        {
            throw RuntimeRepositoryException.CkTypeIdIsAbstract(TenantId, ckTypeGraph.CkTypeId);
        }

        var rtEntity = new TEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = ckTypeGraph.CkTypeId
        };
        foreach (var ckTypeAttributeDto in ckTypeGraph.AllAttributes.Values)
        {
            object? value = null;
            if (ckTypeAttributeDto.DefaultValues != null)
            {
                switch (ckTypeAttributeDto.ValueType)
                {
                    case AttributeValueTypesDto.StringArray:
                    case AttributeValueTypesDto.IntArray:
                        value = ckTypeAttributeDto.DefaultValues;
                        break;
                    default:
                        value = ckTypeAttributeDto.DefaultValues.First();
                        break;
                }
            }

            if (value != null)
            {
                rtEntity.SetAttributeValue(ckTypeAttributeDto.AttributeName, ckTypeAttributeDto.ValueType, value);
            }
        }

        return rtEntity;
    }

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtIds">Object ids of the runtime entities</param>
    /// <param name="dataQueryOperation">Query options for data query</param>
    /// <param name="skip">Amount of items to skip</param>
    /// <param name="take">Amount of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns>Returns a result set of the given type</returns>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds, DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new();

    /// <summary>
    ///     Inserts a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntity">Object to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        rtEntity.CkTypeId = ckTypeId;
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateInsert(rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Inserts multiple runtime entities
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntities">Objects to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task InsertManyRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<TEntity> rtEntities)
        where TEntity : RtEntity, new()
    {
        List<EntityUpdateInfo<TEntity>> entitiesUpdate = new();
        foreach (var rtEntity in rtEntities)
        {
            rtEntity.CkTypeId = ckTypeId;
            entitiesUpdate.Add(EntityUpdateInfo<TEntity>.CreateInsert(rtEntity));
        }

        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters) where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters) where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var rtEntityId = new RtEntityId(ckTypeId, rtId);
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateUpdate(rtEntityId, rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Updates a multiple runtime entities by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilters">A collection of filter objects</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var rtEntityId = new RtEntityId(ckTypeId, rtId);
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateReplace(rtEntityId, rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="dataQueryOperation">Query options for data query</param>
    /// <param name="skip">Amount of items to skip</param>
    /// <param name="take">Amount of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation, int? skip = null, int? take = null) where TEntity : RtEntity, new();
}