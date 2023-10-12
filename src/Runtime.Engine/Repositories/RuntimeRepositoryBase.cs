using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
// ReSharper disable MemberCanBePrivate.Global

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Represents a basic implementation of <see cref="IRuntimeRepository"/>
/// </summary>
public abstract class RuntimeRepositoryBase : IRuntimeRepository
{
    private readonly IBulkRtMutation _bulkRtMutation;

    /// <summary>
    /// Returns the data source of the repository
    /// </summary>
    protected IRepositoryDataSource RepositoryDataSource { get; }

    /// <summary>
    /// Returns the cache service that is used to access the construction kit model
    /// </summary>
    protected ICkCacheService CkCacheService { get; }

    /// <summary>
    /// Creates a new instance of <see cref="RuntimeRepositoryBase"/>
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="ckCacheService">Construction kit cache service</param>
    /// <param name="repositoryDataSource">The corresponding repository data source</param>
    /// <param name="bulkRtMutation"></param>
    protected RuntimeRepositoryBase(string tenantId, ICkCacheService ckCacheService, IRepositoryDataSource repositoryDataSource,
        IBulkRtMutation bulkRtMutation)
    {
        _bulkRtMutation = bulkRtMutation;
        RepositoryDataSource = repositoryDataSource;
        TenantId = tenantId;
        CkCacheService = ckCacheService;
    }

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
    public virtual async Task<TEntity?> GetRtEntityByRtIdAsync<TEntity>(IOctoSession session, OctoObjectId rtId) where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);

        return await rtCollection.DocumentAsync(session, rtId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public abstract Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction);

    /// <inheritdoc />
    public abstract Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);

    /// <inheritdoc />
    public async Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId)
    {
        return await RepositoryDataSource.GetRtAssociationOrDefaultAsync(session, originRtEntityId, targetRtEntityId, ckRoleId)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId, RtEntityId targetRtEntityId)
    {
        return RepositoryDataSource.CreateTransientRtAssociation(originRtEntityId, ckRoleId, targetRtEntityId);
    }

    /// <inheritdoc />
    public RtEntity CreateTransientRtEntity(CkId<CkTypeId> ckTypeId)
    {
        var entityCacheItem = CkCacheService.GetCkType(TenantId, ckTypeId);
        return CreateTransientRtEntity<RtEntity>(entityCacheItem);
    }

    /// <inheritdoc />
    public TEntity CreateTransientRtEntity<TEntity>() where TEntity : RtEntity, new()
    {
        var ckTypeId = RtEntityExtensions.GetCkTypeId<TEntity>();
        if (string.IsNullOrWhiteSpace(ckTypeId.FullName))
        {
            throw RuntimeRepositoryException.CkTypeIdMissingForType(typeof(TEntity));
        }

        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        if (ckTypeGraph == null)
        {
            throw RuntimeRepositoryException.CkTypeIdDoesNotExistInCache(ckTypeId);
        }

        return CreateTransientRtEntity<TEntity>(ckTypeGraph);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync(IOctoSession session, CkId<CkTypeId> ckTypeId, RtEntity rtEntity)
    {
        rtEntity.CkTypeId = ckTypeId;
        var entitiesUpdate = new[] { new EntityUpdateInfo(rtEntity, EntityModOptions.Create) };
        await _bulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[]{}).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity) where TEntity : RtEntity, new()
    {
        rtEntity.CkTypeId = rtEntity.GetCkTypeId();
        var entitiesUpdate = new[] { new EntityUpdateInfo(rtEntity, EntityModOptions.Create) };
        await _bulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[]{}).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChanges(IOctoSession session, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList, IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList,
        OperationResult operationResult)
    {
        await _bulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entityUpdateInfoList, associationUpdateInfoList).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChanges(IOctoSession session, IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult)
    {
        await ApplyChanges(session, new List<EntityUpdateInfo>(), associationUpdateInfoList, operationResult).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyChanges(IOctoSession session, IReadOnlyList<EntityUpdateInfo> entityUpdateInfoList, OperationResult operationResult)
    {
        await ApplyChanges(session, entityUpdateInfoList, new List<AssociationUpdateInfo>(), operationResult).ConfigureAwait(false);
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

            rtEntity.SetAttributeValue(ckTypeAttributeDto.AttributeName, ckTypeAttributeDto.ValueType, value);
        }

        return rtEntity;
    }
}