using System.Linq.Expressions;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     The exception the test persistence raises for an update, replace or delete of an id it does not hold, and the
///     one <see cref="HiddenOrMissingTestRepository" /> returns from its not-found hook, like a real persistence
///     implementation does.
/// </summary>
internal sealed class TestEntityNotFoundException(OctoObjectId rtId)
    : Exception($"Entity '{rtId}' was not found.")
{
    public OctoObjectId RtId { get; } = rtId;
}

/// <summary>
///     A repository on the real write funnel (data-permission guard, <see cref="BulkRtMutation" /> with the real
///     entity rule engine) over an in-memory collection of the types of <see cref="SecretTestModel" />.
///     Association validation is replaced by a stub that refuses the role <see cref="InvalidRoleId" />.
/// </summary>
internal sealed class HiddenOrMissingTestRepository : RuntimeRepositoryBase
{
    internal static readonly RtCkId<CkAssociationRoleId> InvalidRoleId = new("Test/InvalidRole");
    internal const int InvalidRoleMessageNumber = 9999;

    private readonly bool _answersWithTheBaseNotFoundMessage;

    private HiddenOrMissingTestRepository(SecretTestModel model, IRepositoryDataSource dataSource,
        IDataPermissionResolver dataPermissionResolver, bool answersWithTheBaseNotFoundMessage)
        : base(SecretTestModel.TenantId, model.Cache, dataSource,
            new BulkRtMutation(new EntityRuleEngine(model.Cache), new InvalidRoleGraphRuleEngine(), [],
                new SecretWriteNormalizer(SecretTestModel.CreateProtector())),
            dataPermissionResolver)
    {
        _answersWithTheBaseNotFoundMessage = answersWithTheBaseNotFoundMessage;
    }

    internal static HiddenOrMissingTestRepository Create(SecretTestModel model,
        IDataSourceCollection<OctoObjectId, RtEntity> collection, RtDataPolicyTable policyTable,
        bool answersWithTheBaseNotFoundMessage = false)
    {
        var dataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(collection);
        var dataPermissionResolver = A.Fake<IDataPermissionResolver>();
        A.CallTo(() => dataPermissionResolver.GetPolicyTableAsync(A<IRuntimeRepository>._)).Returns(policyTable);
        return new HiddenOrMissingTestRepository(model, dataSource, dataPermissionResolver,
            answersWithTheBaseNotFoundMessage);
    }

    internal static IDataSourceCollection<OctoObjectId, RtEntity> CreateCollection(
        Dictionary<OctoObjectId, RtEntity> storedEntities)
    {
        var collection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => collection.DocumentAsync(A<IOctoSession>._, A<OctoObjectId>._))
            .ReturnsLazily((IOctoSession _, OctoObjectId rtId) => storedEntities.GetValueOrDefault(rtId));
        A.CallTo(() => collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .ReturnsLazily((IOctoSession _, IEnumerable<OctoObjectId> rtIds) => (IReadOnlyList<RtEntity>)rtIds
                .Where(storedEntities.ContainsKey).Select(rtId => storedEntities[rtId]).ToList());
        A.CallTo(() => collection.FindManyAsync(A<IOctoSession>._, A<Expression<Func<RtEntity, bool>>>._,
                A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, Expression<Func<RtEntity, bool>> filter, int? _, int? _) =>
                (ICollection<RtEntity>)storedEntities.Values.Where(filter.Compile()).ToList());
        A.CallTo(() => collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .ReturnsLazily((IOctoSession _, IEnumerable<RtEntity> entities) =>
                ThrowForFirstMissing(storedEntities, entities.Select(entity => entity.RtId)));
        A.CallTo(() => collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .ReturnsLazily((IOctoSession _, IEnumerable<RtEntity> entities) =>
                ThrowForFirstMissing(storedEntities, entities.Select(entity => entity.RtId)));
        A.CallTo(() => collection.DeleteOneAsync(A<IOctoSession>._, A<OctoObjectId>._))
            .ReturnsLazily((IOctoSession _, OctoObjectId rtId) => ThrowForFirstMissing(storedEntities, [rtId]));
        return collection;
    }

    private static Task ThrowForFirstMissing(Dictionary<OctoObjectId, RtEntity> storedEntities,
        IEnumerable<OctoObjectId> rtIds)
    {
        foreach (var rtId in rtIds.Where(rtId => !storedEntities.ContainsKey(rtId)))
        {
            throw new TestEntityNotFoundException(rtId);
        }

        return Task.CompletedTask;
    }

    protected override Exception CreateEntityNotFoundException(RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId,
        RtDataAction action, DeleteStrategies deleteStrategy) =>
        _answersWithTheBaseNotFoundMessage
            ? base.CreateEntityNotFoundException(ckTypeId, rtId, action, deleteStrategy)
            : new TestEntityNotFoundException(rtId);

    protected override Task RefreshCkCacheServiceAsync(ICkCacheService ckCacheService) => Task.CompletedTask;

    public override Task<IOctoSession> GetSessionAsync() => Task.FromResult(A.Fake<IOctoSession>());

    public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByTypeAsync(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions,
        ICollection<NavigationPair> roleIdDirectionPairs, int? skip = null, int? take = null) =>
        throw new NotSupportedException();

    public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, IEnumerable<NavigationPair> roleIdDirectionPairs,
        int? skip = null, int? take = null) => throw new NotSupportedException();

    protected override Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
        RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null) =>
        throw new NotSupportedException();

    protected override Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

    protected override Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

    protected override Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

    protected override Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

    protected override Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
        FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

    protected override Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
        RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions, int? skip = null,
        int? take = null) => throw new NotSupportedException();

    private sealed class InvalidRoleGraphRuleEngine : IGraphRuleEngine
    {
        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(new GraphRuleEngineResult());

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult)
        {
            foreach (var association in associationUpdateInfoList.Where(a => a.RoleId == InvalidRoleId))
            {
                operationResult.AddMessage(new OperationMessage(MessageLevel.Error, null, InvalidRoleMessageNumber,
                    $"Role '{association.RoleId}' is not defined."));
            }

            return Task.FromResult(new GraphRuleEngineResult());
        }

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) =>
            ValidateAsync(session, repositoryDataSource, [], associationUpdateInfoList, originFileResolver,
                operationResult);
    }
}
