using System.Linq.Expressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
/// Implements the <see cref="ILocalRuntimeRepository"/> interface to manage runtime entities that are located in a directory on the local hard disk.
/// </summary>
internal class LocalDirectoryRuntimeRepository : RuntimeRepositoryBase, ILocalRuntimeRepository
{
    /// <summary>
    /// Creates a new instance of <see cref="LocalDirectoryRuntimeRepository"/>.
    /// </summary>
    /// <param name="tenantId">The id of the tenant to request services</param>
    /// <param name="directoryPath">Path to directory the runtime entities are located.</param>
    /// <param name="ckCacheService">Construction kit cache service</param>
    /// <param name="repositoryDataSource">Data source of a local repository</param>
    /// <param name="bulkRtMutation">Bulk runtime mutation implementation</param>
    public LocalDirectoryRuntimeRepository(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        ILocalRepositoryDataSource repositoryDataSource, IBulkRtMutation bulkRtMutation)
        : base(tenantId, ckCacheService, repositoryDataSource, bulkRtMutation)
    {
        DirectoryPath = directoryPath;
    }

    protected override async Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>();
        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        var savedEntities = queryable.Where(CombineFilterExpressions<TEntity>(fieldFilters, ckTypeGraph, LogicalOperator.And));

        List<EntityUpdateInfo<RtEntity>> entitiesUpdate = new();
        foreach (var savedEntity in savedEntities)
        {
            entitiesUpdate.Add(EntityUpdateInfo<RtEntity>.CreateUpdate(savedEntity.ToRtEntityId(), rtEntity));
        }

        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    protected override async Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>();
        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        var savedEntity = queryable.FirstOrDefault(CombineFilterExpressions<TEntity>(fieldFilters, ckTypeGraph, LogicalOperator.And));
        if (savedEntity == null)
        {
            throw RuntimeRepositoryException.FieldFilterDidNotReturnResult(typeof(TEntity), fieldFilters);
        }

        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateReplace(savedEntity.ToRtEntityId(), rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    protected override async Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        DataQueryOperation dataQueryOperation, int? skip = null,
        int? take = null)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>();

        if (dataQueryOperation.AttributeSearchFilter != null)
        {
            throw RuntimeRepositoryException.AttributeFilterNotSupportedByDataSource(typeof(LocalDirectoryRuntimeRepository));
        }

        if (dataQueryOperation.TextSearchFilter != null)
        {
            throw RuntimeRepositoryException.TextFilterNotSupportedByDataSource(typeof(LocalDirectoryRuntimeRepository));
        }

        if (dataQueryOperation.SortOrders != null)
        {
            throw RuntimeRepositoryException.SortOrderNotSupportedByDataSource(typeof(LocalDirectoryRuntimeRepository));
        }


        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        if (dataQueryOperation.FieldFilters != null)
        {
            var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
            queryable = queryable.Where(
                CombineFilterExpressions<TEntity>(dataQueryOperation.FieldFilters, ckTypeGraph, LogicalOperator.And));
        }

        if (skip != null)
        {
            queryable = queryable.Skip(skip.Value);
        }

        if (take != null)
        {
            queryable = queryable.Take(take.Value);
        }

        var resultSet = new ResultSet<TEntity>(queryable, queryable.Count());
        return resultSet;
    }

    protected override async Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters, TEntity rtEntity)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>();
        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        var savedEntity = queryable
            .FirstOrDefault(CombineFilterExpressions<TEntity>(fieldFilters, ckTypeGraph, LogicalOperator.And));
        if (savedEntity == null)
        {
            throw RuntimeRepositoryException.FieldFilterDidNotReturnResult(typeof(TEntity), fieldFilters);
        }

        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateUpdate(savedEntity.ToRtEntityId(), rtEntity) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    protected override async Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);
        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        var rtEntities = queryable
            .Where(CombineFilterExpressions<TEntity>(fieldFilters, ckTypeGraph, LogicalOperator.And));

        List<EntityUpdateInfo<RtEntity>> entitiesUpdate = new();
        foreach (var rtEntity in rtEntities)
        {
            entitiesUpdate.Add(EntityUpdateInfo<RtEntity>.CreateDelete(rtEntity.ToRtEntityId()));
        }

        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    protected override async Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, CkId<CkTypeId> ckTypeId,
        ICollection<FieldFilter> fieldFilters)
    {
        var rtCollection = RepositoryDataSource.GetRtCollection<TEntity>(ckTypeId);
        var ckTypeGraph = CkCacheService.GetCkType(TenantId, ckTypeId);
        var queryable = await rtCollection.AsQueryableAsync(session).ConfigureAwait(false);
        var rtEntity = queryable
            .FirstOrDefault(CombineFilterExpressions<TEntity>(fieldFilters, ckTypeGraph, LogicalOperator.And));
        if (rtEntity == null)
        {
            throw RuntimeRepositoryException.FieldFilterDidNotReturnResult(typeof(TEntity), fieldFilters);
        }

        var entitiesUpdate = new[] { EntityUpdateInfo<TEntity>.CreateDelete(rtEntity.ToRtEntityId()) };
        await BulkRtMutation.ApplyChangesAsync(session, RepositoryDataSource, entitiesUpdate, new AssociationUpdateInfo[] { })
            .ConfigureAwait(false);
    }

    public override Task<IOctoSession> GetSessionAsync()
    {
        return Task.FromResult((IOctoSession)new LocalSession());
    }

    public string DirectoryPath { get; }

    private enum LogicalOperator
    {
        And,
        Or
    }

    private static Expression<Func<TEntity, bool>> CombineFilterExpressions<TEntity>(ICollection<FieldFilter> filters,
        CkTypeGraph ckTypeGraph,
        LogicalOperator logicalOperator) where TEntity : RtEntity
    {
        if (filters.Count == 0)
        {
            return _ => true; // Return a true predicate if no filters are provided
        }

        // Create an initial predicate
        Expression<Func<TEntity, bool>> combinedPredicate = _ => true;

        foreach (var filter in filters)
        {
            // Generate the predicate for the current filter
            if (!ckTypeGraph.AllAttributesByName.TryGetValue(filter.AttributeName, out var attribute))
            {
                throw RuntimeRepositoryException.AttributeWithNameDoesNotExist(ckTypeGraph.CkTypeId, filter.AttributeName);
            }

            var filterExpression = FilterExpression<TEntity>(filter, attribute);

            // Combine the current filter with the existing predicate using the specified logical operator
            if (logicalOperator == LogicalOperator.And)
            {
                combinedPredicate = combinedPredicate.And(filterExpression);
            }
            else if (logicalOperator == LogicalOperator.Or)
            {
                combinedPredicate = combinedPredicate.OrElse(filterExpression);
            }
        }

        return combinedPredicate;
    }

    private static Expression<Func<TEntity, bool>> FilterExpression<TEntity>(FieldFilter filter, CkTypeAttributeGraph ckTypeAttributeGraph)
        where TEntity : RtEntity
    {
        //    Type valueType = AttributeValueConverter.GetDotNetType(ckTypeAttributeGraph.ValueType);

        //   ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "x");
        // MemberExpression property = Expression..Property(parameter, filter.AttributeName);
        //    ConstantExpression value = Expression.Constant(filter.ComparisonValue, valueType);

        switch (filter.Operator)
        {
            case FieldFilterOperator.Equals:
                return rtEntity => rtEntity.Attributes.ContainsKey(filter.AttributeName) &&
                                   Equals(rtEntity.Attributes[filter.AttributeName], filter.ComparisonValue);
            case FieldFilterOperator.NotEquals:
                return x => x.Attributes.ContainsKey(filter.AttributeName) &&
                            !Equals(x.Attributes[filter.AttributeName], filter.ComparisonValue);
            // case FieldFilterOperator.LessEqualThan:
            //     return x=>  x.Attributes.ContainsKey(filter.AttributeName) && Expression.LessThanOrEqual(x.Attributes[filter.AttributeName], filter.ComparisonValue);
            //     
            //     return x => x.GetType().GetProperty(filter.AttributeName)?.GetValue(x)?.ToString() != filter.ComparisonValue;
            // case FieldFilterOperator.In:
            //     var inValues = filter.ComparisonValue.Split(',');
            //     return x => inValues.Contains(x.GetType().GetProperty(filter.AttributeName)?.GetValue(x)?.ToString());
            // case FieldFilterOperator.NotIn:
            //     var notInValues = filter.ComparisonValue.Split(',');
            //     return x => !notInValues.Contains(x.GetType().GetProperty(filter.AttributeName)?.GetValue(x)?.ToString());
            // case FieldFilterOperator.Like:
            //     return x => x.GetType().GetProperty(filter.AttributeName)?.GetValue(x)?.ToString().Contains(filter.ComparisonValue);
            default:
                throw new NotSupportedException("Unsupported filter operator");
        }
    }
}