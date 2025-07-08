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
    private const string CountryTypeId = "Test/Country";
    private const string ContinentTypeId = "Test/Continent";
    private const string ParentChildRoleId = "System/ParentChild";

    #region Successful Validation Tests

    [Fact]
    public async Task ValidateAsync_EntityOnly_ReturnsSuccessfulResult()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var entity = CreateEntity(CountryTypeId);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(entity)], 
            originFileResolver, operationResult);

        // Assert
        Assert.Empty(result.RtAssociationsToCreate);
        Assert.Empty(result.RtAssociationsToDelete);
        Assert.True(operationResult.HasFatalErrors); // Missing mandatory association
    }

    [Fact]
    public async Task ValidateAsync_AssociationOnly_ReturnsSuccessfulResult()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target = CreateEntity(ContinentTypeId);
        SetupEntityRetrieval(dataSource, [origin, target]);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(result.RtAssociationsToCreate);
        Assert.Empty(result.RtAssociationsToDelete);
        Assert.False(operationResult.HasErrors);
    }

    [Fact]
    public async Task ValidateAsync_ValidComposition_CreatesAssociation()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target = CreateEntity(ContinentTypeId);
        SetupEntityRetrieval(dataSource, [origin, target]);
        SetupEmptyAssociations(dataSource);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(origin)],
            [AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(result.RtAssociationsToCreate);
        Assert.Empty(result.RtAssociationsToDelete);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public async Task ValidateAsync_ReplaceAssociation_ValidatesCorrectly()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var oldTarget = CreateEntity(ContinentTypeId);
        var newTarget = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrieval(dataSource, [origin, oldTarget, newTarget]);
        SetupEmptyAssociations(dataSource);
        SetupExistingAssociation(dataSource, origin, oldTarget, ParentChildRoleId);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)],
            [
                AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), oldTarget.ToRtEntityId(), ParentChildRoleId),
                AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), newTarget.ToRtEntityId(), ParentChildRoleId)
            ],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(result.RtAssociationsToCreate);
        Assert.Single(result.RtAssociationsToDelete);
        Assert.Empty(operationResult.Messages);
    }

    #endregion

    #region Error Validation Tests

    [Fact]
    public async Task ValidateAsync_MissingMandatoryAssociation_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);

        // Act
        await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(origin)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(6, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_DeleteMandatoryAssociation_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrieval(dataSource, [origin, target]);
        SetupExistingAssociation(dataSource, origin, target, ParentChildRoleId);
        SetupMultiplicity(dataSource, origin, ParentChildRoleId, GraphDirections.Outbound, CurrentMultiplicity.One);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)],
            [AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(13, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_CreateExistingAssociation_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrieval(dataSource, [origin, target]);
        SetupExistingAssociationInGetRtAssociationsAsync(dataSource, origin, target, ParentChildRoleId);

        // Act
        await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(origin)],
            [AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(16, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_DeleteNonExistentAssociation_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrieval(dataSource, [origin, target]);
        A.CallTo(() => dataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>._, 
            origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId))
            .Returns(Task.FromResult<RtAssociation?>(null));

        // Act
        await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)],
            [AssociationUpdateInfo.CreateDelete(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(15, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_EntityNotFound_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var nonExistentTarget = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrievalWithMissing(dataSource, [origin]);

        // Act
        await engine.ValidateAsync(session, dataSource, 
            [],
            [AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), nonExistentTarget.ToRtEntityId(), ParentChildRoleId)],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(9, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_ViolateOneMultiplicity_AddsError()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var origin = CreateEntity(CountryTypeId);
        var target1 = CreateEntity(ContinentTypeId);
        var target2 = CreateEntity(ContinentTypeId);
        
        SetupEntityRetrieval(dataSource, [origin, target1, target2]);
        SetupEmptyAssociations(dataSource);
        SetupMultiplicity(dataSource, origin, ParentChildRoleId, GraphDirections.Outbound, CurrentMultiplicity.One);

        // Act
        await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateUpdate(origin.ToRtEntityId(), origin)],
            [
                AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target1.ToRtEntityId(), ParentChildRoleId),
                AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target2.ToRtEntityId(), ParentChildRoleId)
            ],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(14, operationResult.Messages[0].MessageNumber);
    }

    #endregion

    #region Entity Deletion Tests

    [Fact]
    public async Task ValidateAsync_DeleteEntity_SchedulesAssociationDeletion()
    {
        // Arrange
        var (engine, session, dataSource, operationResult, originFileResolver) = CreateTestObjects();
        var entity = CreateEntity(CountryTypeId);
        var associatedEntity = CreateEntity(ContinentTypeId);
        var association = CreateAssociation(entity, associatedEntity, ParentChildRoleId);
        
        A.CallTo(() => dataSource.GetRtAssociationsAsync(session, entity.RtId, GraphDirections.Any))
            .Returns([association]);

        // Act
        var result = await engine.ValidateAsync(session, dataSource, 
            [EntityUpdateInfo<RtEntity>.CreateDelete(entity.ToRtEntityId())],
            originFileResolver, operationResult);

        // Assert
        Assert.Single(result.RtAssociationsToDelete);
        Assert.Equal(association, result.RtAssociationsToDelete[0]);
    }

    #endregion

    #region Helper Methods

    private (GraphRuleEngine engine, IOctoSession session, IRepositoryDataSource dataSource, 
        OperationResult operationResult, IOriginFileResolver originFileResolver) CreateTestObjects()
    {
        var ckCacheService = fixture.GetCacheServiceAsync().Result;
        var engine = new GraphRuleEngine(ckCacheService);
        var session = A.Fake<IOctoSession>();
        var dataSource = A.Fake<IRepositoryDataSource>();
        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");

        A.CallTo(() => dataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => dataSource.CreateTransientRtAssociation(A<RtEntityId>._, A<CkId<CkAssociationRoleId>>._, A<RtEntityId>._))
            .ReturnsLazily((RtEntityId origin, CkId<CkAssociationRoleId> roleId, RtEntityId target) =>
                CreateAssociation(origin, target, roleId));

        return (engine, session, dataSource, operationResult, originFileResolver);
    }

    private static RtEntity CreateEntity(string ckTypeId)
    {
        return new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = ckTypeId
        };
    }

    private static RtAssociation CreateAssociation(RtEntity origin, RtEntity target, CkId<CkAssociationRoleId> roleId)
    {
        return CreateAssociation(origin.ToRtEntityId(), target.ToRtEntityId(), roleId);
    }

    private static RtAssociation CreateAssociation(RtEntityId origin, RtEntityId target, CkId<CkAssociationRoleId> roleId)
    {
        return new RtAssociation
        {
            AssociationId = OctoObjectId.GenerateNewId(),
            OriginRtId = origin.RtId,
            OriginCkTypeId = origin.CkTypeId,
            AssociationRoleId = roleId,
            TargetRtId = target.RtId,
            TargetCkTypeId = target.CkTypeId
        };
    }

    private static void SetupEntityRetrieval(IRepositoryDataSource dataSource, IList<RtEntity> entities)
    {
        var collection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .Returns(Task.FromResult<IReadOnlyList<RtEntity>>(entities.ToList()));
        A.CallTo(() => dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._))
            .Returns(collection);
    }

    private static void SetupEntityRetrievalWithMissing(IRepositoryDataSource dataSource, 
        IList<RtEntity> existingEntities)
    {
        var collection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => collection.DocumentsAsync(A<IOctoSession>._, A<IEnumerable<OctoObjectId>>._))
            .ReturnsLazily((IOctoSession _, IEnumerable<OctoObjectId> ids) =>
            {
                var requestedIds = ids.ToHashSet();
                var result = existingEntities.Where(e => requestedIds.Contains(e.RtId)).ToList();
                return Task.FromResult<IReadOnlyList<RtEntity>>(result);
            });
        A.CallTo(() => dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._))
            .Returns(collection);
    }

    private static void SetupEmptyAssociations(IRepositoryDataSource dataSource)
    {
        A.CallTo(() => dataSource.GetRtAssociationsAsync(A<IOctoSession>._, A<IEnumerable<RtOriginTargetPair>>._))
            .Returns(new List<RtAssociation>());
    }

    private static void SetupExistingAssociation(IRepositoryDataSource dataSource, 
        RtEntity origin, RtEntity target, string roleId)
    {
        var association = CreateAssociation(origin, target, roleId);
        A.CallTo(() => dataSource.GetRtAssociationOrDefaultAsync(A<IOctoSession>._, 
            origin.ToRtEntityId(), target.ToRtEntityId(), roleId))
            .Returns(association);
    }

    private static void SetupExistingAssociationInGetRtAssociationsAsync(IRepositoryDataSource dataSource,
        RtEntity origin, RtEntity target, string roleId)
    {
        var association = CreateAssociation(origin, target, roleId);
        A.CallTo(() => dataSource.GetRtAssociationsAsync(A<IOctoSession>._, A<IEnumerable<RtOriginTargetPair>>._))
            .Returns([association]);
    }

    private static void SetupMultiplicity(IRepositoryDataSource dataSource, RtEntity entity, 
        string roleId, GraphDirections direction, CurrentMultiplicity multiplicity)
    {
        var pair = new RtEntityRoleIdDirectionPair(entity.ToRtEntityId(), roleId, direction);
        var result = new RtAssociationsMultiplicityResult(pair, multiplicity);
        
        A.CallTo(() => dataSource.GetRtAssociationsMultiplicityAsync(A<IOctoSession>._, 
            A<IEnumerable<RtEntityRoleIdDirectionPair>>.That.Matches(x => 
                x.Any(p => p.RtEntityId.Equals(entity.ToRtEntityId()) && 
                          p.CkRoleId == roleId && 
                          p.Direction == direction))))
            .Returns([result]);
    }

    #endregion

    #region Legacy Tests (optimiert)

    [Fact]
    public async Task ValidateAsync_Composition_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var target = CreateEntity(ContinentTypeId);
        var origin = CreateEntity(CountryTypeId);

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);
        SetupEmptyAssociations(repositoryDataSource);
        SetupEntityRetrieval(repositoryDataSource, [target, origin]);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(origin)],
            [AssociationUpdateInfo.CreateCreate(origin.ToRtEntityId(), target.ToRtEntityId(), ParentChildRoleId)],
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
        var origin = CreateEntity(CountryTypeId);

        var session = A.Fake<IOctoSession>();
        var repositoryDataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => repositoryDataSource.TenantId).Returns(fixture.TenantId);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var graphRuleEngine = new GraphRuleEngine(ckCacheService);

        var ruleEngineResult = await graphRuleEngine.ValidateAsync(session, repositoryDataSource, 
            [EntityUpdateInfo<RtEntity>.CreateInsert(origin)],
            originFileResolver, operationResult);

        Assert.Empty(ruleEngineResult.RtAssociationsToCreate);
        Assert.Empty(ruleEngineResult.RtAssociationsToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(6, operationResult.Messages[0].MessageNumber);
    }

    #endregion
}
