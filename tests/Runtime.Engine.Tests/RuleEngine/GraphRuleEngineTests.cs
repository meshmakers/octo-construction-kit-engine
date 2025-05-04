using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RuleEngine;

public class GraphRuleEngineTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task ValidateAsync_Composition_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var target = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Continent"
        };
        var origin = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);

        A.CallTo(() => repositoryDataSource.GetRtAssociationsAsync(A<IOctoSession>.Ignored, A<IEnumerable<RtOriginTargetPair>>.Ignored))
            .Returns(new List<RtAssociation>());

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => targetDataSourceCollection.DocumentsAsync(A<IOctoSession>.Ignored, A<IEnumerable<OctoObjectId>>.Ignored))
            .Returns([target]);

        A.CallTo(() => repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
            .Returns(targetDataSourceCollection);


        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateInsert(origin)
            ],
            [
                AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), "System/ParentChild")
            ],
            originFileResolver, operationResult);

        Assert.Single(ruleEngineResult.RtAssociationsToCreate);
        Assert.Empty(ruleEngineResult.RtAssociationsToDelete);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task ValidateAsync_Composition_Missing_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var origin = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);


        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateInsert(origin)
            ],
            originFileResolver, operationResult);

        Assert.Empty(ruleEngineResult.RtAssociationsToCreate);
        Assert.Empty(ruleEngineResult.RtAssociationsToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(6, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_Composition_RemoveAdd_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var oldTarget = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Continent"
        };

        var target = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Continent"
        };
        var origin = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var rtOriginTargetPair = new RtOriginTargetPair(origin.ToRtEntityId(), target.ToRtEntityId(),
            "System/ParentChild");

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => repositoryDataSource.GetRtAssociationsAsync(A<IOctoSession>.Ignored, A<IEnumerable<RtOriginTargetPair>>.Ignored))
            .Returns(new List<RtAssociation>());

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => targetDataSourceCollection.DocumentsAsync(A<IOctoSession>.Ignored, A<IEnumerable<OctoObjectId>>.Ignored))
            .Returns([target, origin, oldTarget]);
        A.CallTo(() => repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
            .Returns(targetDataSourceCollection);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateInsert(origin)
            ],
            [
                AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), oldTarget.ToRtEntityId(),
                    "System/ParentChild"),
                AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), "System/ParentChild")
            ],
            originFileResolver, operationResult);

        Assert.Single(ruleEngineResult.RtAssociationsToCreate);
        Assert.Single(ruleEngineResult.RtAssociationsToDelete);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task ValidateAsync_Composition_Remove_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var oldTarget = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Continent"
        };

        var origin = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var rtEntityRoleIdDirectionPair = new RtEntityRoleIdDirectionPair(origin.ToRtEntityId(), "System/ParentChild",
            GraphDirections.Outbound);

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => repositoryDataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>.Ignored,
                origin.ToRtEntityId(), oldTarget.ToRtEntityId(), "System/ParentChild"))
            .Returns(new RtAssociation());
        A.CallTo(() =>
            repositoryDataSource.GetRtAssociationsMultiplicityAsync(A<IOctoSession>.Ignored, A<IEnumerable<RtEntityRoleIdDirectionPair>>.That.Matches(x=> x.Count() == 1 && x.First().Direction == GraphDirections.Outbound)
                )).Returns([new RtAssociationsMultiplicityResult(rtEntityRoleIdDirectionPair, CurrentMultiplicity.One)
        ]);

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => targetDataSourceCollection.DocumentsAsync(A<IOctoSession>.Ignored, A<IEnumerable<OctoObjectId>>.Ignored))
            .Returns([oldTarget]);

        A.CallTo(() => repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
            .Returns(targetDataSourceCollection);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)
            ],
            [
                AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), oldTarget.ToRtEntityId(),
                    "System/ParentChild"),
            ],
            originFileResolver, operationResult);

        Assert.Empty(ruleEngineResult.RtAssociationsToCreate);
        Assert.Single(ruleEngineResult.RtAssociationsToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(13, operationResult.Messages[0].MessageNumber);
    }
}