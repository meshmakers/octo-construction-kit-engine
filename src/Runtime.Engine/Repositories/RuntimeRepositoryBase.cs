using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Represents a basic implementation of <see cref="IRuntimeRepository"/>
/// </summary>
public abstract class RuntimeRepositoryBase : IRuntimeRepository
{
    private readonly IEntityRuleEngine _entityRuleEngine;

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
    /// <param name="entityRuleEngine">Entity rule engine object</param>
    protected RuntimeRepositoryBase(string tenantId, ICkCacheService ckCacheService, IRepositoryDataSource repositoryDataSource,
        IEntityRuleEngine entityRuleEngine)
    {
        _entityRuleEngine = entityRuleEngine;
        RepositoryDataSource = repositoryDataSource;
        TenantId = tenantId;
        CkCacheService = ckCacheService;
    }

    /// <inheritdoc />
    public string TenantId { get; }


    /// <inheritdoc />
    public abstract Task<IOctoSession> GetSessionAsync();

    /// <inheritdoc />
    public abstract Task<RtEntity?> GetRtEntityByRtIdAsync(IOctoSession session, RtEntityId rtEntityId);

    /// <inheritdoc />
    public abstract Task<IEnumerable<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction);

    /// <inheritdoc />
    public abstract Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);

    /// <inheritdoc />
    public abstract Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, CkId<CkAssociationRoleId> ckRoleId);

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId, RtEntityId targetRtEntityId)
    {
        return new RtAssociation
        {
            AssociationRoleId = ckRoleId,
            OriginCkTypeId = originRtEntityId.CkTypeId,
            OriginRtId = originRtEntityId.RtId,
            TargetCkTypeId = targetRtEntityId.CkTypeId,
            TargetRtId = targetRtEntityId.RtId
        };
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
        var rtCollection = RepositoryDataSource.GetRtCollection<RtEntity>(ckTypeId);
        PrepareEntityForModification(rtEntity);
        OperationResult operationResult = new();
        var ruleEngineResult = await _entityRuleEngine.ValidateAsync(
            TenantId,
            new[] { new EntityUpdateInfo(rtEntity, EntityModOptions.Create) },
            operationResult).ConfigureAwait(false);
        
        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);
        
        foreach (var entity in ruleEngineResult.RtEntitiesToCreate)
        {
            await rtCollection.InsertAsync(session, entity).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public virtual async Task InsertOneRtEntityAsync<TEntity>(IOctoSession session, TEntity rtEntity) where TEntity : RtEntity, new()
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>();
        PrepareEntityForModification(rtEntity);
        
        OperationResult operationResult = new();
        var ruleEngineResult = await _entityRuleEngine.ValidateAsync(
            TenantId,
            new[] { new EntityUpdateInfo<TEntity>(rtEntity, EntityModOptions.Create) },
            operationResult).ConfigureAwait(false);
        
        RuntimeRepositoryException.ThrowIfOperationResultError(operationResult);
        
        foreach (var entity in ruleEngineResult.RtEntitiesToCreate)
        {
            await rtCollection.InsertAsync(session, entity).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// Prepares an runtime entity for modification
    /// </summary>
    /// <param name="rtEntity">Object to insert</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity"/></typeparam>
    protected void PrepareEntityForModification<TEntity>(TEntity rtEntity) where TEntity : RtEntity, new()
    {
        rtEntity.RtChangedDateTime = DateTime.UtcNow;
        if (!rtEntity.RtCreationDateTime.HasValue)
        {
            rtEntity.RtCreationDateTime = rtEntity.RtChangedDateTime;
        }
        if (string.IsNullOrWhiteSpace(rtEntity.CkTypeId.FullName)) {
            rtEntity.CkTypeId = rtEntity.GetCkTypeId();
        }
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