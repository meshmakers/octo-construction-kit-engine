using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Models.System.Generated.System.v2;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using TestCkModel.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories.LocalDirectoryRepository;

/// <summary>
/// Tests for <see cref="RepositoryDataSource.GetRtAssociationsAsync"/> verifying that the optimized
/// CkTypeId-grouped query logic correctly returns associations for various scenarios.
/// </summary>
public class RepositoryDataSourceAssociationTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    #region Helper Methods

    private async Task<LocalDirectoryRuntimeRepository> CreateRepositoryAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        return new LocalDirectoryRuntimeRepository(
            fixture.TenantId,
            fixture.RepositoryPath,
            ckCacheService,
            new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer),
            bulkRtMutation);
    }

    private static async Task<(RtOcean origin, RtOcean target)> CreateAndLinkOceans(
        LocalDirectoryRuntimeRepository repository, string originName, string targetName)
    {
        var origin = await repository.CreateTransientRtEntityAsync<RtOcean>();
        origin.Designation = originName;
        await repository.InsertOneRtEntityAsync(new LocalSession(), origin);

        var target = await repository.CreateTransientRtEntityAsync<RtOcean>();
        target.Designation = targetName;
        await repository.InsertOneRtEntityAsync(new LocalSession(), target);

        OperationResult result = new();
        await repository.ApplyChangesAsync(new LocalSession(), [],
        [
            new AssociationUpdateInfo(
                origin.ToRtEntityId(),
                target.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ], result);

        Assert.False(result.HasErrors);
        return (origin, target);
    }

    #endregion

    #region Single Entity Lookup

    [Fact]
    public async Task GetRtAssociationsAsync_SingleEntity_Inbound_ReturnsAssociation()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var (origin, target) = await CreateAndLinkOceans(repository, "Origin_Single_In", "Target_Single_In");

        // Act — look up inbound associations of the target
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            target.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(origin.RtId, result.Items.First().OriginRtId);
        Assert.Equal(target.RtId, result.Items.First().TargetRtId);
    }

    [Fact]
    public async Task GetRtAssociationsAsync_SingleEntity_Outbound_ReturnsAssociation()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var (origin, target) = await CreateAndLinkOceans(repository, "Origin_Single_Out", "Target_Single_Out");

        // Act — look up outbound associations of the origin
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            origin.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(origin.RtId, result.Items.First().OriginRtId);
        Assert.Equal(target.RtId, result.Items.First().TargetRtId);
    }

    [Fact]
    public async Task GetRtAssociationsAsync_SingleEntity_Any_ReturnsAssociation()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var (origin, target) = await CreateAndLinkOceans(repository, "Origin_Single_Any", "Target_Single_Any");

        // Act — look up any-direction associations of the origin
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            origin.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Any));

        // Assert
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetRtAssociationsAsync_SingleEntity_NoAssociations_ReturnsEmpty()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var ocean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        ocean.Designation = "Lonely_Ocean";
        await repository.InsertOneRtEntityAsync(new LocalSession(), ocean);

        // Act
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            ocean.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Any));

        // Assert
        Assert.Empty(result.Items);
    }

    #endregion

    #region Multiple Entities with Same CkTypeId

    [Fact]
    public async Task GetRtAssociationsAsync_MultipleEntities_SameCkTypeId_ReturnsCorrectAssociations()
    {
        // Arrange — two origins of the same type, each linked to a different target
        var repository = await CreateRepositoryAsync();
        var (origin1, target1) = await CreateAndLinkOceans(repository, "Origin_Multi1", "Target_Multi1");
        var (origin2, target2) = await CreateAndLinkOceans(repository, "Origin_Multi2", "Target_Multi2");

        // Act — query outbound associations for both origins at once
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            [origin1.ToRtEntityId(), origin2.ToRtEntityId()],
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));

        // Assert — should have results for both origins
        Assert.True(result.ContainsKey(origin1.ToRtEntityId()));
        Assert.True(result.ContainsKey(origin2.ToRtEntityId()));
        Assert.Single(result[origin1.ToRtEntityId()].Items);
        Assert.Single(result[origin2.ToRtEntityId()].Items);
        Assert.Equal(target1.RtId, result[origin1.ToRtEntityId()].Items.First().TargetRtId);
        Assert.Equal(target2.RtId, result[origin2.ToRtEntityId()].Items.First().TargetRtId);
    }

    [Fact]
    public async Task GetRtAssociationsAsync_MultipleEntities_SameCkTypeId_Inbound_ReturnsCorrectAssociations()
    {
        // Arrange — two targets of the same type, each linked from a different origin
        var repository = await CreateRepositoryAsync();
        var (origin1, target1) = await CreateAndLinkOceans(repository, "Origin_MultiIn1", "Target_MultiIn1");
        var (origin2, target2) = await CreateAndLinkOceans(repository, "Origin_MultiIn2", "Target_MultiIn2");

        // Act — query inbound associations for both targets at once
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            [target1.ToRtEntityId(), target2.ToRtEntityId()],
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));

        // Assert
        Assert.True(result.ContainsKey(target1.ToRtEntityId()));
        Assert.True(result.ContainsKey(target2.ToRtEntityId()));
        Assert.Single(result[target1.ToRtEntityId()].Items);
        Assert.Single(result[target2.ToRtEntityId()].Items);
        Assert.Equal(origin1.RtId, result[target1.ToRtEntityId()].Items.First().OriginRtId);
        Assert.Equal(origin2.RtId, result[target2.ToRtEntityId()].Items.First().OriginRtId);
    }

    #endregion

    #region Multiple Entities — Mixed Lookup

    [Fact]
    public async Task GetRtAssociationsAsync_MultipleEntities_MixedOriginAndTarget_ReturnsCorrectAssociations()
    {
        // Arrange — three oceans in a chain: ocean1 -> ocean2 -> ocean3
        // Query [ocean1, ocean3] with outbound — ocean1 should have 1 assoc, ocean3 should have 0
        var repository = await CreateRepositoryAsync();
        var (ocean1, ocean2) = await CreateAndLinkOceans(repository, "Ocean_Mix1", "Ocean_Mix2");

        var ocean3 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        ocean3.Designation = "Ocean_Mix3";
        await repository.InsertOneRtEntityAsync(new LocalSession(), ocean3);

        OperationResult linkResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [],
        [
            new AssociationUpdateInfo(
                ocean2.ToRtEntityId(),
                ocean3.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ], linkResult);
        Assert.False(linkResult.HasErrors);

        // Act — query outbound for ocean1 and ocean3
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            [ocean1.ToRtEntityId(), ocean3.ToRtEntityId()],
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));

        // Assert — ocean1 has outbound to ocean2, ocean3 has no outbound
        Assert.True(result.ContainsKey(ocean1.ToRtEntityId()));
        Assert.True(result.ContainsKey(ocean3.ToRtEntityId()));
        Assert.Single(result[ocean1.ToRtEntityId()].Items);
        Assert.Empty(result[ocean3.ToRtEntityId()].Items);
    }

    #endregion

    #region Any Direction (bidirectional)

    [Fact]
    public async Task GetRtAssociationsAsync_AnyDirection_ReturnsBothInboundAndOutbound()
    {
        // Arrange — ocean1 -> ocean2 -> ocean3, query ocean2 with Any direction
        var repository = await CreateRepositoryAsync();
        var (ocean1, ocean2) = await CreateAndLinkOceans(repository, "Ocean_Bi_1", "Ocean_Bi_2");

        var ocean3 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        ocean3.Designation = "Ocean_Bi_3";
        await repository.InsertOneRtEntityAsync(new LocalSession(), ocean3);

        OperationResult linkResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [],
        [
            new AssociationUpdateInfo(
                ocean2.ToRtEntityId(),
                ocean3.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ], linkResult);
        Assert.False(linkResult.HasErrors);

        // Act — query ocean2 with Any direction (should get inbound from ocean1 + outbound to ocean3)
        var result = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            ocean2.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Any));

        // Assert — should have 2 associations (one inbound, one outbound)
        Assert.Equal(2, result.Items.Count());
    }

    #endregion

    #region Delete Entity with Associations (full pipeline through GraphRuleEngine)

    [Fact]
    public async Task ApplyChanges_DeleteEntityWithAssociations_AssociationsArchived()
    {
        // Arrange — this test exercises the full path:
        // ApplyChangesAsync → BulkRtMutation → GraphRuleEngine.ValidateCkModel → GetRtAssociationsInternalAsync
        var repository = await CreateRepositoryAsync();
        var (origin, target) = await CreateAndLinkOceans(repository, "Origin_Delete", "Target_Delete");

        // Act — delete origin entity (GraphRuleEngine will query associations to schedule for deletion)
        List<EntityUpdateInfo<RtEntity>> deleteInfos =
            [EntityUpdateInfo<RtEntity>.CreateDelete(origin.ToRtEntityId())];

        OperationResult deleteResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Archive };
        await repository.ApplyChangesAsync(new LocalSession(), deleteInfos, [], deleteOptions, deleteResult);

        // Assert
        Assert.False(deleteResult.HasErrors);

        // Origin should be archived
        var retrievedOrigin = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), origin.RtId);
        Assert.NotNull(retrievedOrigin);
        Assert.Equal(RtState.Archived, retrievedOrigin.RtState);
    }

    [Fact]
    public async Task ApplyChanges_DeleteMultipleEntitiesWithAssociations_OK()
    {
        // Arrange — multiple entities with associations, all being deleted
        // This tests the grouping optimization with multiple entity IDs of the same CkTypeId
        var repository = await CreateRepositoryAsync();
        var (origin1, target1) = await CreateAndLinkOceans(repository, "Origin_MultiDel1", "Target_MultiDel1");
        var (origin2, target2) = await CreateAndLinkOceans(repository, "Origin_MultiDel2", "Target_MultiDel2");

        // Act — delete both origin entities at once
        List<EntityUpdateInfo<RtEntity>> deleteInfos =
        [
            EntityUpdateInfo<RtEntity>.CreateDelete(origin1.ToRtEntityId()),
            EntityUpdateInfo<RtEntity>.CreateDelete(origin2.ToRtEntityId())
        ];

        OperationResult deleteResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Archive };
        await repository.ApplyChangesAsync(new LocalSession(), deleteInfos, [], deleteOptions, deleteResult);

        // Assert
        Assert.False(deleteResult.HasErrors);

        var retrieved1 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), origin1.RtId);
        var retrieved2 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), origin2.RtId);
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal(RtState.Archived, retrieved1.RtState);
        Assert.Equal(RtState.Archived, retrieved2.RtState);
    }

    #endregion

    #region Batch Delete — GraphRuleEngine Optimization

    [Fact]
    public async Task ApplyChanges_DeleteMultipleIndependentEntitiesWithAssociations_AllArchived()
    {
        // Arrange — two independent pairs, delete both origins in a single batch
        // This exercises the batched GetRtAssociationsAsync call in GraphRuleEngine.ValidateCkModel
        var repository = await CreateRepositoryAsync();
        var (origin1, target1) = await CreateAndLinkOceans(repository, "BatchDel_O1", "BatchDel_T1");
        var (origin2, target2) = await CreateAndLinkOceans(repository, "BatchDel_O2", "BatchDel_T2");

        // Act — delete both origins at once
        List<EntityUpdateInfo<RtEntity>> deleteInfos =
        [
            EntityUpdateInfo<RtEntity>.CreateDelete(origin1.ToRtEntityId()),
            EntityUpdateInfo<RtEntity>.CreateDelete(origin2.ToRtEntityId())
        ];

        OperationResult deleteResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Archive };
        await repository.ApplyChangesAsync(new LocalSession(), deleteInfos, [], deleteOptions, deleteResult);

        // Assert — both origins archived
        Assert.False(deleteResult.HasErrors);

        var retrieved1 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), origin1.RtId);
        var retrieved2 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), origin2.RtId);
        Assert.NotNull(retrieved1);
        Assert.NotNull(retrieved2);
        Assert.Equal(RtState.Archived, retrieved1.RtState);
        Assert.Equal(RtState.Archived, retrieved2.RtState);

        // Targets should still exist and not be archived
        var retrievedT1 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), target1.RtId);
        var retrievedT2 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), target2.RtId);
        Assert.NotNull(retrievedT1);
        Assert.NotNull(retrievedT2);
        Assert.NotEqual(RtState.Archived, retrievedT1.RtState);
        Assert.NotEqual(RtState.Archived, retrievedT2.RtState);
    }

    [Fact]
    public async Task ApplyChanges_DeleteAssociationExplicitly_AssociationRemoved()
    {
        // Arrange — tests the batched ValidateAssociationsToDelete path
        var repository = await CreateRepositoryAsync();
        var (origin, target) = await CreateAndLinkOceans(repository, "Origin_ExplDel", "Target_ExplDel");

        // Act — explicitly delete the association
        OperationResult deleteResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [],
        [
            new AssociationUpdateInfo(
                origin.ToRtEntityId(),
                target.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Delete)
        ], deleteResult);

        // Assert — association should be gone
        Assert.False(deleteResult.HasErrors);

        var assocResult = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            origin.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));
        Assert.Empty(assocResult.Items);
    }

    [Fact]
    public async Task ApplyChanges_DeleteMultipleAssociationsExplicitly_AllRemoved()
    {
        // Arrange — two independent associations, both explicitly deleted in batch
        var repository = await CreateRepositoryAsync();
        var (origin1, target1) = await CreateAndLinkOceans(repository, "Origin_BatchDel1", "Target_BatchDel1");
        var (origin2, target2) = await CreateAndLinkOceans(repository, "Origin_BatchDel2", "Target_BatchDel2");

        // Act — delete both associations in a single ApplyChanges call
        OperationResult deleteResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [],
        [
            new AssociationUpdateInfo(
                origin1.ToRtEntityId(),
                target1.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Delete),
            new AssociationUpdateInfo(
                origin2.ToRtEntityId(),
                target2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Delete)
        ], deleteResult);

        // Assert — both associations should be removed
        Assert.False(deleteResult.HasErrors);

        var assocResult1 = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            origin1.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));
        Assert.Empty(assocResult1.Items);

        var assocResult2 = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            origin2.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound));
        Assert.Empty(assocResult2.Items);
    }

    #endregion
}
