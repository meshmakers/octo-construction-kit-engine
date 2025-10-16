using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;

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
    protected RuntimeRepositoryBase(string tenantId, ICkCacheService ckCacheService,
        IRepositoryDataSource repositoryDataSource,
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

    /// <summary>
    /// Loads the cache for the tenant using the provided cache service.
    /// </summary>
    /// <param name="cacheService">The cache service to use for loading the cache.</param>
    public async Task LoadCacheForTenantAsync(ICkCacheService cacheService)
    {
        await RefreshCkCacheServiceAsync(cacheService).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public abstract Task<IOctoSession> GetSessionAsync();

    /// <inheritdoc />
    public virtual async Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId)
    {
        var ckTypeGraph = await GetCkTypeGraphAsync(rtEntityId.CkTypeId).ConfigureAwait(false);
        var rtCollection = RepositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);

        return await rtCollection.DocumentAsync(session, rtEntityId.RtId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        var ckTypeGraph = await GetCkTypeGraphAsync(ckTypeId).ConfigureAwait(false);
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeGraph);

        return await rtCollection.DocumentAsync(session, rtId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<RtEntity>> GetRtEntitiesByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds, DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null)
    {
        return await GetRtEntitiesByIdAsync<RtEntity>(session, ckTypeId, rtIds, dataQueryOperation, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
        IReadOnlyList<OctoObjectId> rtIds,
        DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return await GetRtEntitiesByIdAsync<TEntity>(session, ckTypeId, rtIds, dataQueryOperation, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<RtEntity>> GetRtEntitiesByTypeAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation, int? skip = null,
        int? take = null)
    {
        return await GetRtEntitiesByTypeAsync<RtEntity>(session, ckTypeId, dataQueryOperation, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        DataQueryOperation dataQueryOperation,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return await GetRtEntitiesByTypeAsync<TEntity>(session, ckTypeId, dataQueryOperation, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        RtEntityId rtEntityId, GraphDirections direction, int? skip = null, int? take = null)
    {
        var r = await RepositoryDataSource.GetRtAssociationsAsync(session, [rtEntityId], direction).ConfigureAwait(false);

        return r.Values.FirstOrDefault() ??
               new ResultSet<RtAssociation>(new List<RtAssociation>(), 0, null, null);
    }

    /// <inheritdoc />
    public async Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, GraphDirections direction, int? skip = null,
        int? take = null)
    {
        return await RepositoryDataSource.GetRtAssociationsAsync(session, rtEntityIds, direction, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, GraphDirections direction, RtCkId<CkAssociationRoleId> roleId, int? skip = null,
        int? take = null)
    {
        return await RepositoryDataSource.GetRtAssociationsAsync(session, rtEntityIds, direction, roleId)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        RtEntityId rtEntityId, GraphDirections direction, RtCkId<CkAssociationRoleId> roleId, int? skip = null,
        int? take = null)
    {
        var r = await RepositoryDataSource.GetRtAssociationsAsync(session, [rtEntityId], direction, roleId)
            .ConfigureAwait(false);

        return r.Values.FirstOrDefault() ??
               new ResultSet<RtAssociation>(new List<RtAssociation>(), 0, null, null);
    }

    /// <inheritdoc />
    public async Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId)
    {
        return await RepositoryDataSource
            .GetRtAssociationOrDefaultAsync(session, originRtEntityId, targetRtEntityId, ckRoleId)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IQueryable<TEntity>> AsQueryableAsync<TEntity>(IOctoSession? session = null)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        var ckTypeGraph = await GetCkTypeGraphAsync(ckTypeId).ConfigureAwait(false);
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeGraph);

        return await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IQueryable<TEntity> AsQueryable<TEntity>(IOctoSession? session = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        var t = GetCkTypeGraphAsync(ckTypeId);
        t.Wait();
        var ckTypeGraph = t.Result;
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeGraph);

        return rtCollection.AsQueryable(session);
    }

    /// <inheritdoc />
    public abstract Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByTypeAsync(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, DataQueryOperation dataQueryOperation,
        ICollection<NavigationPair> roleIdDirectionPairs, int? skip = null, int? take = null);

    /// <inheritdoc />
    public abstract Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
        DataQueryOperation dataQueryOperation, IEnumerable<NavigationPair> roleIdDirectionPairs, int? skip = null,
        int? take = null);

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId)
    {
        return RepositoryDataSource.CreateTransientRtAssociation(originRtEntityId, ckRoleId, targetRtEntityId);
    }

    /// <inheritdoc />
    public async Task<RtEntity> CreateTransientRtEntityByRtCkIdAsync(RtCkId<CkTypeId> rtCkTypeId)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var ckTypeGraph = cacheService.GetRtCkType(TenantId, rtCkTypeId);
        return CreateTransientRtEntity<RtEntity>(ckTypeGraph);
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
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        if (string.IsNullOrWhiteSpace(ckTypeId.FullName))
        {
            throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(TEntity));
        }

        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var ckTypeGraph = cacheService.GetRtCkType(TenantId, ckTypeId);
        if (ckTypeGraph == null)
        {
            throw RuntimeRepositoryException.RtCkTypeIdDoesNotExistInCache(ckTypeId);
        }

        return CreateTransientRtEntity<TEntity>(ckTypeGraph);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, RtEntity rtEntity)
    {
        await InsertOneRtEntityAsync<RtEntity>(session, rtEntity.GetRtCkTypeId(), rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        await InsertOneRtEntityAsync(session, rtEntity.GetRtCkTypeId(), rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InsertManyRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        ICollection<RtEntity> rtEntities)
    {
        await InsertManyRtEntityAsync<RtEntity>(session, ckTypeId, rtEntities).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task InsertManyRtEntityAsync<TEntity>(IOctoSession session, ICollection<TEntity> rtEntities)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        await InsertManyRtEntityAsync(session, ckTypeId, rtEntities).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        RtEntity rtEntity)
    {
        await ReplaceOneRtEntityByIdAsync<RtEntity>(session, rtEntity.GetRtCkTypeId(), rtId, rtEntity)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        await ReplaceOneRtEntityByIdAsync(session, rtEntity.GetRtCkTypeId(), rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        RtEntity rtEntity)
    {
        await ReplaceOneRtEntityAsync(session, rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
                fieldFilterCriteria, rtEntity)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        await ReplaceOneRtEntityAsync(session, ckTypeId, fieldFilterCriteria, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityByIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        RtEntity rtEntity)
    {
        await UpdateOneRtEntityByIdAsync<RtEntity>(session, ckTypeId, rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId, TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        await UpdateOneRtEntityByIdAsync(session, ckTypeId, rtId, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        RtEntity rtEntity)
    {
        await UpdateOneRtEntityAsync(session, rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
                fieldFilterCriteria, rtEntity)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        await UpdateOneRtEntityAsync(session, ckTypeId, fieldFilterCriteria, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateManyRtEntityAsync(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        RtEntity rtEntity)
    {
        await UpdateManyRtEntityAsync(session, rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet(),
                fieldFilterCriteria, rtEntity)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        await UpdateManyRtEntityAsync(session, ckTypeId, fieldFilterCriteria, rtEntity).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityByRtIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var entitiesUpdate = new[] { EntityUpdateInfo<RtEntity>.CreateDelete(new RtEntityId(ckTypeId, rtId)) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateDelete(new RtEntityId(ckTypeId, rtId)) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria)
    {
        await DeleteOneRtEntityAsync<RtEntity>(session, ckTypeId, fieldFilterCriteria).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        await DeleteOneRtEntityAsync<TEntity>(session, ckTypeId, fieldFilterCriteria).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteManyRtEntitiesAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria)
    {
        await DeleteManyRtEntitiesAsync<RtEntity>(session, ckTypeId, fieldFilterCriteria).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria)
        where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        await DeleteManyRtEntitiesAsync<TEntity>(session, ckTypeId, fieldFilterCriteria).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entityUpdateInfoList,
                associationUpdateInfoList, BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        await ApplyChangesAsync(session, new List<IEntityUpdateInfo<RtEntity>>(), associationUpdateInfoList,
                operationResult)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        await ApplyChangesAsync(session, entityUpdateInfoList, new List<AssociationUpdateInfo>(), operationResult)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OctoObjectId> UploadTemporaryLargeBinaryAsync(IOctoSession session, string filename, string contentType,
        DateTime expiryDateTime,
        Stream stream, CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.UploadTemporaryBinaryAsync(session, filename, contentType, expiryDateTime, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OctoObjectId> ReplaceTemporaryLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, Stream stream,
        CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.ReplaceTemporaryLargeBinaryAsync(session, filename, contentType, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        await RepositoryDataSource.DeleteTemporaryLargeBinaryAsync(session, largeBinaryId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime, CancellationToken cancellationToken = default)
    {
        await RepositoryDataSource.DeleteExpiredTemporaryLargeBinariesAsync(session, expiryDateTime, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken = default)
    {
        await RepositoryDataSource.DeleteAllTemporaryLargeBinariesAsync(session, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IDownloadStreamHandler> DownloadLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.DownloadBinaryAsync(session, largeBinaryId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IBinaryInfo?> GetTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId binaryId,
        CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.GetTemporaryBinaryAsync(session, binaryId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IBinaryInfo?> GetTemporaryLargeBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.GetTemporaryBinaryAsync(session, fileName, cancellationToken);
    }

    /// <summary>
    /// Gets the construction kit type graph from the cache service
    /// </summary>
    /// <param name="ckTypeId">The ck type id</param>
    /// <returns></returns>
    /// <exception cref="RuntimeRepositoryException">CkTypeId does not exist in cache</exception>
    public async Task<CkTypeGraph> GetCkTypeGraphAsync(RtCkId<CkTypeId> ckTypeId)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var ckTypeGraph = cacheService.GetRtCkType(TenantId, ckTypeId);
        if (ckTypeGraph == null)
        {
            throw RuntimeRepositoryException.RtCkTypeIdDoesNotExistInCache(ckTypeId);
        }

        return ckTypeGraph;
    }

    /// <summary>
    ///     Returns the cache service used to access the construction kit model
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
            CkTypeId = ckTypeGraph.CkTypeId.ToRtCkId()
        };
        foreach (var ckTypeAttributeDto in ckTypeGraph.AllAttributes.Values)
        {
            object? value = null;
            if (ckTypeAttributeDto.DefaultValues != null && ckTypeAttributeDto.DefaultValues.Any())
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
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns>Returns a result set of the given type</returns>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId,
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
    protected virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        rtEntity.CkTypeId = ckTypeId;
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateInsert(rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
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
    protected virtual async Task InsertManyRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        ICollection<TEntity> rtEntities)
        where TEntity : RtEntity, new()
    {
        List<EntityUpdateInfo<TEntity>> entitiesUpdate = [];
        foreach (var rtEntity in rtEntities)
        {
            rtEntity.CkTypeId = ckTypeId;
            entitiesUpdate.Add(EntityUpdateInfo<TEntity>.CreateInsert(rtEntity));
        }

        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria) where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria) where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Updates a single runtime entity. Only attributes of the entity that are set in the update object are updated.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task UpdateOneRtEntityByIdAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        OctoObjectId rtId,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var rtEntityId = new RtEntityId(ckTypeId, rtId);
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateUpdate(rtEntityId, rtEntity) };
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Updates the multiple runtime entities by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="rtEntity">Runtime entity object as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) where TEntity : RtEntity, new();

    /// <summary>
    ///     Replace a single runtime entity
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtId">Runtime object id</param>
    /// <param name="rtEntity">Runtime object that is used as replacement</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected virtual async Task ReplaceOneRtEntityByIdAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        OctoObjectId rtId,
        TEntity rtEntity)
        where TEntity : RtEntity, new()
    {
        var rtEntityId = new RtEntityId(ckTypeId, rtId);
        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateReplace(rtEntityId, rtEntity) };
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="dataQueryOperation">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation, int? skip = null, int? take = null) where TEntity : RtEntity, new();


    #region Advanced functionality

    /// <inheritdoc />
    public async Task<AggregatedBulkImportResult> BulkInsertRtEntitiesAsync(IOctoSession session,
        IEnumerable<RtEntity> rtEntityList, BulkOperationOptions options)
    {
        var results = new List<IBulkImportResult>();
        foreach (var groupedEntities in rtEntityList.GroupBy(x => x.CkTypeId))
        {
            if (groupedEntities.Key == null)
            {
                throw PersistenceException.CkTypeIdNotSet();
            }

            var ckTypeGraph = await GetCkTypeGraphAsync(groupedEntities.Key).ConfigureAwait(false);

            results.Add(await RepositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph)
                .BulkImportAsync(session, groupedEntities, options).ConfigureAwait(false));
        }

        return new AggregatedBulkImportResult(results);
    }

    /// <inheritdoc />
    public async Task<IBulkImportResult> BulkRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtAssociation> rtAssociations, BulkOperationOptions options)
    {
        return await RepositoryDataSource.RtAssociations.BulkImportAsync(session, rtAssociations, options).ConfigureAwait(false);
    }

    #endregion Advanced functionality
}