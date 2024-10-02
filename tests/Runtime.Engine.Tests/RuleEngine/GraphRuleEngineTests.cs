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
        var origin  = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => repositoryDataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>.Ignored,
                origin.ToRtEntityId(), target.ToRtEntityId(), "System/ParentChild"))
            .Returns<RtAssociation?>(null);

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(()=> targetDataSourceCollection.DocumentAsync(A<IOctoSession>.Ignored, A<OctoObjectId>.Ignored))
            .Returns(target);

        A.CallTo(()=> repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
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
        
        var origin  = new RtEntity
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
        var origin  = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => repositoryDataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>.Ignored,
                origin.ToRtEntityId(), target.ToRtEntityId(), "System/ParentChild"))
            .Returns<RtAssociation?>(null);

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => targetDataSourceCollection.DocumentAsync(A<IOctoSession>.Ignored, oldTarget.RtId))
            .Returns(oldTarget);
        A.CallTo(() => targetDataSourceCollection.DocumentAsync(A<IOctoSession>.Ignored, target.RtId))
            .Returns(target);
        A.CallTo(() => targetDataSourceCollection.DocumentAsync(A<IOctoSession>.Ignored, origin.RtId))
            .Returns(origin);
            
        A.CallTo(()=> repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
            .Returns(targetDataSourceCollection);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);
        
        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateInsert(origin)
            ],
            [
                AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), oldTarget.ToRtEntityId(), "System/ParentChild"),
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
        
        var origin  = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = "Test/Country"
        };

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => repositoryDataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>.Ignored,
                origin.ToRtEntityId(), oldTarget.ToRtEntityId(), "System/ParentChild"))
            .Returns<RtAssociation?>(new RtAssociation());
        A.CallTo(()=> repositoryDataSource.GetCurrentRtAssociationMultiplicityAsync(A<IOctoSession>.Ignored,
                origin.ToRtEntityId(), "System/ParentChild", GraphDirections.Outbound))
            .Returns(CurrentMultiplicity.One);

        var targetDataSourceCollection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(()=> targetDataSourceCollection.DocumentAsync(A<IOctoSession>.Ignored, A<OctoObjectId>.Ignored))
            .Returns(oldTarget);

        A.CallTo(()=> repositoryDataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>.Ignored))
            .Returns(targetDataSourceCollection);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);
        
        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, [
                EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)
            ],
            [
                AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), oldTarget.ToRtEntityId(), "System/ParentChild"),
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