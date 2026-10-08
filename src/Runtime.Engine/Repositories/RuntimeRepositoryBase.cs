using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Security;
using Microsoft.Extensions.Logging;

// ReSharper disable MemberCanBePrivate.Global

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Represents a basic implementation of <see cref="IRuntimeRepository" />
/// </summary>
public abstract class RuntimeRepositoryBase : IRuntimeRepository
{
    /// <summary>
    ///     Timestamp of the last CK cache reload triggered by a type lookup miss, per tenant
    ///     (AB#5415). Process-wide on purpose: a repository instance is created per call, so
    ///     instance state could never rate-limit anything.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTime> LastStaleCkCacheReloadUtc = new();

    /// <summary>
    ///     Minimum distance between two stale-cache reloads of the same tenant. Settable for tests.
    /// </summary>
    internal static TimeSpan StaleCkCacheReloadCooldown { get; set; } = TimeSpan.FromMinutes(1);

    private readonly ICkCacheService _ckCacheService;
    private readonly IDataPermissionResolver? _dataPermissionResolver;
    private readonly IAuditEventSink? _auditEventSink;
    private readonly ILogger? _logger;

    /// <summary>
    ///     Creates a new instance of <see cref="RuntimeRepositoryBase" />
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="ckCacheService">Construction kit cache service</param>
    /// <param name="repositoryDataSource">The corresponding repository data source</param>
    /// <param name="bulkRtMutation"></param>
    /// <param name="dataPermissionResolver">
    ///     Optional data-permission resolver (AB#4973); when absent, write-side data permissions are
    ///     not enforced (backward-compatible)
    /// </param>
    /// <param name="auditEventSink">Optional audit sink for AuditOnly permission violations</param>
    /// <param name="logger">
    ///     Optional logger; used to report a stale CK cache that had to be reloaded (AB#5415)
    /// </param>
    protected RuntimeRepositoryBase(string tenantId, ICkCacheService ckCacheService,
        IRepositoryDataSource repositoryDataSource,
        IBulkRtMutation bulkRtMutation,
        IDataPermissionResolver? dataPermissionResolver = null,
        IAuditEventSink? auditEventSink = null,
        ILogger? logger = null)
    {
        BulkRtMutation = bulkRtMutation;
        RepositoryDataSource = repositoryDataSource;
        TenantId = tenantId;
        _ckCacheService = ckCacheService;
        _dataPermissionResolver = dataPermissionResolver;
        _auditEventSink = auditEventSink;
        _logger = logger;
    }

    /// <summary>
    ///     The bulk mutation implementation
    /// </summary>
    protected IBulkRtMutation BulkRtMutation { get; }

    /// <summary>
    ///     Optional audit sink for data-permission audit events (AB#4973).
    /// </summary>
    protected IAuditEventSink? AuditEventSink => _auditEventSink;

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
        IReadOnlyList<OctoObjectId> rtIds, RtEntityQueryOptions rtEntityQueryOptions,
        int? skip = null, int? take = null)
    {
        return await GetRtEntitiesByIdAsync<RtEntity>(session, ckTypeId, rtIds, rtEntityQueryOptions, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
        IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return await GetRtEntitiesByIdAsync<TEntity>(session, ckTypeId, rtIds, rtEntityQueryOptions, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<RtEntity>> GetRtEntitiesByTypeAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        RtEntityQueryOptions rtEntityQueryOptions, int? skip = null,
        int? take = null)
    {
        return await GetRtEntitiesByTypeAsync<RtEntity>(session, ckTypeId, rtEntityQueryOptions, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        RtEntityQueryOptions rtEntityQueryOptions,
        int? skip = null, int? take = null) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return await GetRtEntitiesByTypeAsync<TEntity>(session, ckTypeId, rtEntityQueryOptions, skip, take)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        RtEntityId rtEntityId,
        RtAssociationExtendedQueryOptions associationExtendedQueryOptions)
    {
        var r = await RepositoryDataSource.GetRtAssociationsAsync(session, [rtEntityId], associationExtendedQueryOptions)
            .ConfigureAwait(false);

        return r.Values.FirstOrDefault() ??
               new ResultSet<RtAssociation>(new List<RtAssociation>(), 0, null, null);
    }

    /// <inheritdoc />
    public async Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, RtAssociationExtendedQueryOptions associationExtendedQueryOptions)
    {
        return await RepositoryDataSource.GetRtAssociationsAsync(session, rtEntityIds, associationExtendedQueryOptions)
            .ConfigureAwait(false);
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
        RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions,
        ICollection<NavigationPair> roleIdDirectionPairs, int? skip = null, int? take = null);

    /// <inheritdoc />
    public abstract Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, IEnumerable<NavigationPair> roleIdDirectionPairs, int? skip = null,
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
    public async Task DeleteOneRtEntityByRtIdAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        DeleteOptions deleteOptions)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var resolvedCkTypeId =
            await ResolveConcreteCkTypeIdForDeleteAsync(session, cacheService, ckTypeId, rtId).ConfigureAwait(false);
        if (resolvedCkTypeId == null)
        {
            return;
        }

        var entitiesUpdate = new[] { EntityUpdateInfo<RtEntity>.CreateDelete(new RtEntityId(resolvedCkTypeId, rtId)) };
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.FromDeleteOptions(deleteOptions))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteOneRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId,
        DeleteOptions deleteOptions) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        var resolvedCkTypeId =
            await ResolveConcreteCkTypeIdForDeleteAsync(session, cacheService, ckTypeId, rtId).ConfigureAwait(false);
        if (resolvedCkTypeId == null)
        {
            return;
        }

        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateDelete(new RtEntityId(resolvedCkTypeId, rtId)) };
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.FromDeleteOptions(deleteOptions))
            .ConfigureAwait(false);
    }

    // For delete-by-id with an abstract CK type, find the concrete CkTypeId of the entity at this
    // RtId. The validator rejects abstract types on insert/update/replace (correct: a concrete
    // subtype is required), but for delete the RtId is enough to identify the concrete entity.
    // Returns null when the entity does not exist, so the caller can no-op (matches the idempotent
    // semantics of the underlying collection.DeleteOneAsync).
    //
    // The probe order is backend-aware:
    //   1. Try the abstract base's collection first. In storage backends that share a single
    //      collection per inheritance tree (MongoDB: e.g. RtEntity_SystemIdentityIdentityProvider),
    //      this is one cheap query that resolves the concrete type via the document's CkTypeId
    //      discriminator. In per-concrete-type backends (LocalDirectory) it is a no-op miss.
    //   2. Fall back to iterating concrete derived types, probing each collection by RtId. First
    //      hit wins; misses are cheap.
    private async Task<RtCkId<CkTypeId>?> ResolveConcreteCkTypeIdForDeleteAsync(IOctoSession session,
        ICkCacheService cacheService, RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        var ckTypeGraph = cacheService.GetRtCkType(TenantId, ckTypeId);
        if (ckTypeGraph == null || !ckTypeGraph.IsAbstract)
        {
            return ckTypeId;
        }

        var abstractCollection = RepositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph);
        var existing = await abstractCollection.DocumentAsync(session, rtId).ConfigureAwait(false);
        if (existing?.CkTypeId != null)
        {
            return existing.CkTypeId;
        }

        foreach (var derivedTypeId in ckTypeGraph.GetAllDerivedTypes(false))
        {
            var derivedRtCkId = derivedTypeId.ToRtCkId();
            var derivedGraph = cacheService.GetRtCkType(TenantId, derivedRtCkId);
            if (derivedGraph == null || derivedGraph.IsAbstract)
            {
                continue;
            }

            var derivedCollection = RepositoryDataSource.GetRtCollection<RtEntity>(derivedGraph);
            var match = await derivedCollection.DocumentAsync(session, rtId).ConfigureAwait(false);
            if (match != null)
            {
                return match.CkTypeId ?? derivedRtCkId;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task DeleteOneRtEntityAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions)
    {
        return DeleteOneRtEntityAsync<RtEntity>(session, ckTypeId, fieldFilterCriteria, deleteOptions);
    }

    /// <inheritdoc />
    public Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return DeleteOneRtEntityAsync<TEntity>(session, ckTypeId, fieldFilterCriteria, deleteOptions);
    }

    /// <inheritdoc />
    public Task DeleteManyRtEntitiesAsync(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions)
    {
        return DeleteManyRtEntitiesAsync<RtEntity>(session, ckTypeId, fieldFilterCriteria, deleteOptions);
    }

    /// <inheritdoc />
    public Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, FieldFilterCriteria fieldFilterCriteria,
        DeleteOptions deleteOptions) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetRtCkTypeId<TEntity>();

        return DeleteManyRtEntitiesAsync<TEntity>(session, ckTypeId, fieldFilterCriteria, deleteOptions);
    }

    /// <summary>
    ///     The single write funnel with data-permission enforcement (AB#4973): system sessions and
    ///     tenants without policies pass through untouched (dormant guarantee); denied changes reject
    ///     the whole set atomically with a <see cref="RuntimeRepositoryException" />.
    /// </summary>
    private async Task ApplyChangesGuardedAsync(IOctoSession session, ICkCacheService cacheService,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        BulkRtMutationOptions options)
    {
        var securityContext = session.GetSecurityContext();
        if (!securityContext.IsSystem && _dataPermissionResolver != null)
        {
            var policyTable = await _dataPermissionResolver.GetPolicyTableAsync(this).ConfigureAwait(false);
            if (policyTable.HasRules)
            {
                var guardResult = new OperationResult();
                await DataPermissionWriteGuard.CheckAsync(this, cacheService, _auditEventSink, session,
                        securityContext, policyTable, entityUpdateInfoList, associationUpdateInfoList,
                        guardResult)
                    .ConfigureAwait(false);
                RuntimeRepositoryException.ThrowIfOperationResultError(guardResult);
            }
        }

        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, cacheService, entityUpdateInfoList,
                associationUpdateInfoList, options)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        DeleteOptions deleteOptions,
        OperationResult operationResult)
    {
        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        await ApplyChangesGuardedAsync(session, cacheService, entityUpdateInfoList,
                associationUpdateInfoList, BulkRtMutationOptions.FromDeleteOptions(deleteOptions))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        return ApplyChangesAsync(session, entityUpdateInfoList, associationUpdateInfoList, DeleteOptions.Default,
            operationResult);
    }

    /// <inheritdoc />
    public Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        return ApplyChangesAsync(session, new List<IEntityUpdateInfo<RtEntity>>(), associationUpdateInfoList,
            operationResult);
    }

    /// <inheritdoc />
    public Task ApplyChangesAsync(IOctoSession session,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        OperationResult operationResult)
    {
        return ApplyChangesAsync(session, entityUpdateInfoList,
            new List<AssociationUpdateInfo>(), operationResult);
    }

    /// <inheritdoc />
    public Task ApplyChangesAsync(IOctoSession session, IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        DeleteOptions deleteOptions,
        OperationResult operationResult)
    {
        return ApplyChangesAsync(session, entityUpdateInfoList,
            new List<AssociationUpdateInfo>(), deleteOptions, operationResult);
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
    public async Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime,
        CancellationToken cancellationToken = default)
    {
        await RepositoryDataSource.DeleteExpiredTemporaryLargeBinariesAsync(session, expiryDateTime, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session,
        CancellationToken cancellationToken = default)
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
    public Task<IBinaryInfo?> GetLargeBinaryInfoAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        return RepositoryDataSource.GetBinaryInfoAsync(session, largeBinaryId, cancellationToken);
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
    /// <exception cref="CkCacheException">CkTypeId does not exist in the cache</exception>
    public async Task<CkTypeGraph> GetCkTypeGraphAsync(RtCkId<CkTypeId> ckTypeId)
    {
        // Read BEFORE the load: a cache this call had to load itself is as fresh as the repository
        // can make it, so a miss on it is real and reloading again would only repeat it.
        var wasAlreadyLoaded = _ckCacheService.IsTenantLoaded(TenantId);

        var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
        if (cacheService.TryGetRtCkType(TenantId, ckTypeId, out var ckTypeGraph))
        {
            return ckTypeGraph;
        }

        // AB#5415: a loaded tenant cache is never re-read on its own. Every path that invalidates
        // it after a CK model import - the communication controller's CkModelChanged broadcast to
        // the adapters, the PreUpdateTenant/PosUpdateTenant pair inside the services - is a
        // best-effort push with no acknowledgement, so a broken hub connection, a controller
        // restart between the Pre and the Pos message, or an event published before this process
        // started all lose it silently. The cache then keeps answering from the model that was
        // current when it was loaded, and every execution fails with the exact error the import
        // just repaired until someone runs ClearCache by hand. A miss is the one moment the
        // staleness becomes observable, so reload once here and look again before giving up.
        if (wasAlreadyLoaded
            && await TryReloadStaleCkCacheAsync(cacheService, ckTypeId).ConfigureAwait(false)
            && cacheService.TryGetRtCkType(TenantId, ckTypeId, out ckTypeGraph))
        {
            return ckTypeGraph;
        }

        // Let the cache raise its own miss: several services catch CkCacheException to mean "this
        // tenant never imported that CK library" (the AI token-lease reader, the adapter's caller
        // binding, identity group assignment), and throwing anything else here would slip past
        // every one of them. The former RuntimeRepositoryException branch below the lookup was
        // dead code for the same reason - GetRtCkType threw before a null could be returned.
        return cacheService.GetRtCkType(TenantId, ckTypeId);
    }

    /// <summary>
    ///     Reloads this tenant's CK model cache after a type lookup missed, at most once per
    ///     <see cref="StaleCkCacheReloadCooldown" /> (AB#5415). Returns true when this call actually
    ///     reloaded, i.e. when a second lookup is worth doing.
    /// </summary>
    /// <remarks>
    ///     The cooldown is the whole point of the guard: a pipeline that references a genuinely
    ///     unknown type misses on every single execution - roughly 100 per second on the adapter that
    ///     produced AB#5415 - and a reload is a full model resolve against the repository. Without it
    ///     the self-heal would cost far more than the stale cache it repairs.
    /// </remarks>
    private async Task<bool> TryReloadStaleCkCacheAsync(ICkCacheService cacheService, RtCkId<CkTypeId> ckTypeId)
    {
        if (!TryEnterStaleCkCacheReload(TenantId, StaleCkCacheReloadCooldown))
        {
            return false;
        }

        _logger?.LogWarning(
            "Construction Kit type '{RtCkTypeId}' is missing from the loaded CK cache of tenant '{TenantId}'. " +
            "Reloading the model - the cache was not invalidated after the type was added (AB#5415)",
            ckTypeId, TenantId);

        // Unload first: the model loader short-circuits on an already loaded tenant, so a refresh
        // without it is a no-op. A concurrent execution that finds the cache unloaded meanwhile
        // reloads it through GetCkCacheServiceAsync, which the loader serialises on one load.
        if (cacheService.IsTenantLoaded(TenantId))
        {
            cacheService.Unload(TenantId);
        }

        await RefreshCkCacheServiceAsync(cacheService).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    ///     Claims the right to reload <paramref name="tenantId" />'s CK cache, or returns false when
    ///     another miss did so within <paramref name="cooldown" />. The compare-and-swap loop keeps
    ///     concurrent executions - the normal case on an adapter - down to a single reload.
    /// </summary>
    private static bool TryEnterStaleCkCacheReload(string tenantId, TimeSpan cooldown)
    {
        var now = DateTime.UtcNow;
        while (true)
        {
            if (LastStaleCkCacheReloadUtc.TryGetValue(tenantId, out var last))
            {
                if (now - last < cooldown)
                {
                    return false;
                }

                if (LastStaleCkCacheReloadUtc.TryUpdate(tenantId, now, last))
                {
                    return true;
                }
            }
            else if (LastStaleCkCacheReloadUtc.TryAdd(tenantId, now))
            {
                return true;
            }
        }
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

    /// <inheritdoc />
    public virtual Task<(IReadOnlyList<RtEntity> Entities, bool IsSharedCollection)> GetRtEntitiesByTypeForMigrationAsync(
        IOctoSession session, RtCkId<CkTypeId> rtCkTypeId)
    {
        throw new NotSupportedException(
            "GetRtEntitiesByTypeForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can access entity collections without CK cache validation.");
    }

    /// <inheritdoc />
    public virtual Task DeleteOneRtEntityForMigrationAsync(
        IOctoSession session, RtCkId<CkTypeId> rtCkTypeId, OctoObjectId rtId)
    {
        throw new NotSupportedException(
            "DeleteOneRtEntityForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can access entity collections without CK cache validation.");
    }

    /// <inheritdoc />
    public virtual Task InsertOneRtEntityForMigrationAsync(
        IOctoSession session, RtCkId<CkTypeId> rtCkTypeId, RtEntity rtEntity)
    {
        throw new NotSupportedException(
            "InsertOneRtEntityForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can access entity collections without CK cache validation.");
    }

    /// <inheritdoc />
    public virtual Task UpdateCkTypeIdForMigrationAsync(
        IOctoSession session, OctoObjectId rtId, RtCkId<CkTypeId> newCkTypeId)
    {
        throw new NotSupportedException(
            "UpdateCkTypeIdForMigrationAsync is not supported by this repository implementation.");
    }

    /// <inheritdoc />
    public virtual Task<int> UpdateAssociationCkTypeIdsForMigrationAsync(
        IOctoSession session, RtCkId<CkTypeId> oldCkTypeId, RtCkId<CkTypeId> newCkTypeId)
    {
        throw new NotSupportedException(
            "UpdateAssociationCkTypeIdsForMigrationAsync is not supported by this repository implementation.");
    }

    /// <inheritdoc />
    public virtual Task<bool> DropCollectionIfEmptyForMigrationAsync(RtCkId<CkTypeId> rtCkTypeId)
    {
        throw new NotSupportedException(
            "DropCollectionIfEmptyForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can drop collections without CK cache validation.");
    }

    /// <inheritdoc />
    public virtual Task RewriteAttributeValueForMigrationAsync(
        IOctoSession session,
        RtCkId<CkTypeId> rtCkTypeId,
        OctoObjectId rtId,
        string attributeId,
        object? newValue)
    {
        throw new NotSupportedException(
            "RewriteAttributeValueForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can mutate a single attribute slot without CK cache validation.");
    }

    /// <inheritdoc />
    public virtual Task<bool> RewriteAttributeValueIfUnchangedForMigrationAsync(
        IOctoSession session,
        RtCkId<CkTypeId> rtCkTypeId,
        OctoObjectId rtId,
        string attributeId,
        object? expectedValue,
        object? newValue)
    {
        throw new NotSupportedException(
            "RewriteAttributeValueIfUnchangedForMigrationAsync is not supported by this repository implementation. " +
            "This method requires a repository that can conditionally mutate a single attribute slot without CK cache validation.");
    }

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
            // AB#5528/AB#5532: a transient entity never carries a Secret value - Secret attributes take
            // no default (the compiler forbids them), so nothing reaches a Secret slot here and the
            // write step has nothing to do. Values assigned later are normalised when the entity is
            // written (BulkRtMutation, BulkInsertRtEntitiesAsync).
            if (ckTypeAttributeDto.DefaultValues != null && ckTypeAttributeDto.DefaultValues.Any()
                && ckTypeAttributeDto.ValueType != AttributeValueTypesDto.Secret)
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
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns>Returns a result set of the given type</returns>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId,
        IReadOnlyList<OctoObjectId> rtIds, RtEntityQueryOptions rtEntityQueryOptions,
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
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
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
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Deletes all entities with the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="deleteOptions">Options of the delete operation</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) where TEntity : RtEntity, new();

    /// <summary>
    ///     Deletes a single runtime entity by the given filter options
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="fieldFilterCriteria">Object that contains the filter criteria</param>
    /// <param name="deleteOptions">Options of the delete operation</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns></returns>
    protected abstract Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) where TEntity : RtEntity, new();

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
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
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
        await ApplyChangesGuardedAsync(session, cacheService, entitiesUpdate,
                [], BulkRtMutationOptions.Default)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets entities based on the query options.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="ckTypeId">Construction kit type id</param>
    /// <param name="rtEntityQueryOptions">Query options for data query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    protected abstract Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId,
        RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null) where TEntity : RtEntity, new();


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

            // AB#5532: the bulk import bypasses BulkRtMutation, so it runs the Secret write step itself
            // (insert semantics: "" is not set, plaintext is encrypted, protected
            // values - e.g. preserved by the import's upsert preservation - pass through). Required
            // secrets are not enforced here: the import reports missing mandatory attributes itself
            // (AB#4772, ImportRtModelCommand.FindMissingMandatoryAttributes).
            var cacheService = await GetCkCacheServiceAsync().ConfigureAwait(false);
            var entities = groupedEntities.ToList();
            if (BulkRtMutation.SecretWriteNormalizer.HasSecretAttributes(cacheService, TenantId, ckTypeGraph))
            {
                foreach (var entity in entities)
                {
                    BulkRtMutation.SecretWriteNormalizer.Normalize(cacheService, TenantId, ckTypeGraph, entity,
                        SecretWriteOperation.Insert);
                }
            }

            // AB#5945: the bulk import bypasses the pre-document modifications as well, so the
            // engine-computed display fields are evaluated here. Without it every seeded entity
            // (blueprint install, UpdateBlueprint Safe/Merge/Full, ImportRt) is written without
            // rtDisplayName, and an upsert (full replace) wipes the value a previous save computed.
            // The AutoIncrement modifier stays deliberately excluded: imports carry their numbers.
            DisplayFieldUpdateRecompute.ComputeForFullDocuments(ckTypeGraph, entities);

            results.Add(await RepositoryDataSource.GetRtCollection<RtEntity>(ckTypeGraph)
                .BulkImportAsync(session, entities, options).ConfigureAwait(false));
        }

        return new AggregatedBulkImportResult(results);
    }

    /// <inheritdoc />
    public async Task<IBulkImportResult> BulkRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtAssociation> rtAssociations, BulkOperationOptions options)
    {
        return await RepositoryDataSource.RtAssociations.BulkImportAsync(session, rtAssociations, options)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRtAssociationsByIdAsync(IOctoSession session, IEnumerable<OctoObjectId> associationIds)
    {
        await RepositoryDataSource.RtAssociations.DeleteOneAsync(session, associationIds).ConfigureAwait(false);
    }

    #endregion Advanced functionality
}