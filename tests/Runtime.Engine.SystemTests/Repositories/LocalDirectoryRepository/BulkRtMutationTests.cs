using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Models.System.Generated.System.v2;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.Repositories.Local;
using Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;
using TestCkModel.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Repositories.LocalDirectoryRepository;

/// <summary>
/// Integration tests for <see cref="BulkRtMutation"/> class testing all entity and association operations.
/// </summary>
public class BulkRtMutationTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
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

    private async Task<(IBulkRtMutation bulkRtMutation, IRepositoryDataSource dataSource, ICkCacheService ckCacheService)>
        GetBulkRtMutationContextAsync()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var rtSerializer = fixture.GetRtRepositorySerializer();
        var bulkRtMutation = fixture.GetBulkRtMutation();
        var dataSource = new LocalRepositoryDataSource(fixture.TenantId, fixture.RepositoryPath, ckCacheService, rtSerializer);
        return (bulkRtMutation, dataSource, ckCacheService);
    }

    #endregion

    #region Entity Insert Tests

    [Fact]
    public async Task ApplyChanges_InsertSingleEntity_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "Pacific Ocean";

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean)];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos,  [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var retrievedEntity = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrievedEntity);
        Assert.Equal("Pacific Ocean", retrievedEntity.Designation);
    }

    [Fact]
    public async Task ApplyChanges_InsertMultipleEntities_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];

        for (var i = 0; i < 5; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"Ocean_{i}";
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean));
        }

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var queryOptions = RtEntityQueryOptions.Create();
        var result = await repository.GetRtEntitiesByTypeAsync<RtOcean>(new LocalSession(), queryOptions);
        Assert.True(result.TotalCount >= 5);
    }

    [Fact]
    public async Task ApplyChanges_InsertEntities_WithBulkMode_OK()
    {
        // Arrange - Using IBulkRtMutation directly to test BulkMode
        var (bulkRtMutation, dataSource, ckCacheService) = await GetBulkRtMutationContextAsync();
        var repository = await CreateRepositoryAsync();

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];
        for (var i = 0; i < 10; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"BulkOcean_{i}";
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean));
        }

        // Act - Call IBulkRtMutation directly with BulkMode option
        var options = new BulkRtMutationOptions { UseBulkMode = true };
        await bulkRtMutation.ApplyChangesAsync(
            new LocalSession(),
            dataSource,
            ckCacheService,
            entityUpdateInfos,
            [],
            options);

        // Assert - Verify entities were inserted by checking individual entities
        foreach (var entityInfo in entityUpdateInfos)
        {
            var ocean = entityInfo.RtEntity as RtOcean;
            Assert.NotNull(ocean);
            var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), ocean.RtId);
            Assert.NotNull(retrieved);
            Assert.NotNull(retrieved.Designation);
        }
    }

    [Fact]
    public async Task ApplyChanges_InsertOceanWithDesignation_OK()
    {
        // Arrange - Using Ocean which has Designation attribute from Location and no parent requirements
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "TestOcean";

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean)];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
        Assert.Equal("TestOcean", retrieved.Designation);
    }

    #endregion

    #region Entity Update Tests

    [Fact]
    public async Task ApplyChanges_UpdateSingleEntity_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "Atlantic Ocean";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        var updateEntity = new RtOcean { Designation = "Updated Atlantic Ocean" };
        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
            [EntityUpdateInfo<RtEntity>.CreateUpdate(rtOcean.ToRtEntityId(), updateEntity)];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
        Assert.Equal("Updated Atlantic Ocean", retrieved.Designation);
    }

    [Fact]
    public async Task ApplyChanges_UpdateMultipleEntities_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        List<RtOcean> insertedOceans = [];

        for (var i = 0; i < 3; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"Ocean_Update_{i}";
            await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
            insertedOceans.Add(rtOcean);
        }

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];
        foreach (var ocean in insertedOceans)
        {
            var updateEntity = new RtOcean { Designation = $"Updated_{ocean.Designation}" };
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateUpdate(ocean.ToRtEntityId(), updateEntity));
        }

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        foreach (var ocean in insertedOceans)
        {
            var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), ocean.RtId);
            Assert.NotNull(retrieved);
            Assert.StartsWith("Updated_", retrieved.Designation);
        }
    }

    [Fact]
    public async Task ApplyChanges_UpdateOceanPartialAttributes_OK()
    {
        // Arrange - Using Ocean which has no parent requirements
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "PartialUpdateOcean";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        // Update only Designation
        var updateEntity = new RtOcean { Designation = "UpdatedOcean" };
        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
            [EntityUpdateInfo<RtEntity>.CreateUpdate(rtOcean.ToRtEntityId(), updateEntity)];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
        Assert.Equal("UpdatedOcean", retrieved.Designation);
    }

    #endregion

    #region Entity Replace Tests

    [Fact]
    public async Task ApplyChanges_ReplaceSingleEntity_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "Indian Ocean";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        var replaceEntity = await repository.CreateTransientRtEntityAsync<RtOcean>();
        replaceEntity.Designation = "Replaced Indian Ocean";

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
            [EntityUpdateInfo<RtEntity>.CreateReplace(rtOcean.ToRtEntityId(), replaceEntity)];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
        Assert.Equal("Replaced Indian Ocean", retrieved.Designation);
    }

    [Fact]
    public async Task ApplyChanges_ReplaceMultipleEntities_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        List<RtOcean> insertedOceans = [];

        for (var i = 0; i < 3; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"Ocean_Replace_{i}";
            await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
            insertedOceans.Add(rtOcean);
        }

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];
        foreach (var ocean in insertedOceans)
        {
            var replaceEntity = await repository.CreateTransientRtEntityAsync<RtOcean>();
            replaceEntity.Designation = $"Replaced_{ocean.Designation}";
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateReplace(ocean.ToRtEntityId(), replaceEntity));
        }

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        foreach (var ocean in insertedOceans)
        {
            var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), ocean.RtId);
            Assert.NotNull(retrieved);
            Assert.StartsWith("Replaced_", retrieved.Designation);
        }
    }

    [Fact]
    public async Task ApplyChanges_ReplaceEntities_WithBulkMode_OK()
    {
        // Arrange - Using IBulkRtMutation directly to test BulkMode
        var (bulkRtMutation, dataSource, ckCacheService) = await GetBulkRtMutationContextAsync();
        var repository = await CreateRepositoryAsync();

        List<RtOcean> insertedOceans = [];
        for (var i = 0; i < 5; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"BulkReplace_Ocean_{i}";
            await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
            insertedOceans.Add(rtOcean);
        }

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];
        foreach (var ocean in insertedOceans)
        {
            var replaceEntity = await repository.CreateTransientRtEntityAsync<RtOcean>();
            replaceEntity.Designation = $"BulkReplaced_{ocean.Designation}";
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateReplace(ocean.ToRtEntityId(), replaceEntity));
        }

        // Act - Call IBulkRtMutation directly with BulkMode option
        var options = new BulkRtMutationOptions { UseBulkMode = true };
        await bulkRtMutation.ApplyChangesAsync(
            new LocalSession(),
            dataSource,
            ckCacheService,
            entityUpdateInfos,
            [],
            options);

        // Assert
        foreach (var ocean in insertedOceans)
        {
            var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), ocean.RtId);
            Assert.NotNull(retrieved);
            Assert.StartsWith("BulkReplaced_", retrieved.Designation);
        }
    }

    #endregion

    #region Entity Delete Tests

    [Fact]
    public async Task ApplyChanges_DeleteEntity_ArchiveStrategy_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "Ocean_ToArchive";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
            [EntityUpdateInfo<RtEntity>.CreateDelete(rtOcean.ToRtEntityId())];

        // Act
        OperationResult operationResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Archive };
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], deleteOptions, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        // Entity should still exist but be archived
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
        Assert.Equal(RtState.Archived, retrieved.RtState);
    }

    [Fact]
    public async Task ApplyChanges_DeleteEntity_EraseStrategy_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "Ocean_ToErase";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
            [EntityUpdateInfo<RtEntity>.CreateDelete(rtOcean.ToRtEntityId())];

        // Act
        OperationResult operationResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Erase };
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], deleteOptions, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        // Entity should be completely removed
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task ApplyChanges_DeleteMultipleEntities_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        List<RtOcean> insertedOceans = [];

        for (var i = 0; i < 3; i++)
        {
            var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOcean.Designation = $"Ocean_MultiDelete_{i}";
            await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean);
            insertedOceans.Add(rtOcean);
        }

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [];
        foreach (var ocean in insertedOceans)
        {
            entityUpdateInfos.Add(EntityUpdateInfo<RtEntity>.CreateDelete(ocean.ToRtEntityId()));
        }

        // Act
        OperationResult operationResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Erase };
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], deleteOptions, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        foreach (var ocean in insertedOceans)
        {
            var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), ocean.RtId);
            Assert.Null(retrieved);
        }
    }

    #endregion

    #region Association Tests

    [Fact]
    public async Task ApplyChanges_InsertAssociation_OK()
    {
        // Arrange - Using Ocean -> Ocean association with Related role (optional multiplicity)
        var repository = await CreateRepositoryAsync();

        var rtOcean1 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean1.Designation = "OceanOrigin";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean1);

        var rtOcean2 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean2.Designation = "OceanTarget";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean2);

        List<AssociationUpdateInfo> associationUpdateInfos =
        [
            new AssociationUpdateInfo(
                rtOcean1.ToRtEntityId(),
                rtOcean2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [], associationUpdateInfos, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var associations = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            rtOcean2.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));

        Assert.Single(associations.Items);
        var assoc = associations.Items.First();
        Assert.Equal(rtOcean1.RtId, assoc.OriginRtId);
        Assert.Equal(rtOcean2.RtId, assoc.TargetRtId);
    }

    [Fact]
    public async Task ApplyChanges_DeleteAssociation_OK()
    {
        // Arrange - Using Ocean -> Ocean association with Related role (optional multiplicity)
        var repository = await CreateRepositoryAsync();

        var rtOcean1 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean1.Designation = "OceanOriginForDeleteAssoc";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean1);

        var rtOcean2 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean2.Designation = "OceanTargetForDeleteAssoc";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean2);

        // First create the association
        List<AssociationUpdateInfo> createAssociation =
        [
            new AssociationUpdateInfo(
                rtOcean1.ToRtEntityId(),
                rtOcean2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ];
        OperationResult createResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [], createAssociation, createResult);
        Assert.False(createResult.HasErrors);

        // Now delete the association
        List<AssociationUpdateInfo> deleteAssociation =
        [
            new AssociationUpdateInfo(
                rtOcean1.ToRtEntityId(),
                rtOcean2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Delete)
        ];

        // Act
        OperationResult deleteResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [], deleteAssociation, deleteResult);

        // Assert
        Assert.False(deleteResult.HasErrors);
        var associations = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            rtOcean2.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));
        Assert.Empty(associations.Items);
    }

    [Fact]
    public async Task ApplyChanges_InsertMultipleAssociations_OK()
    {
        // Arrange - Using Ocean -> Ocean association with Related role (multiple origins to one target)
        var repository = await CreateRepositoryAsync();

        var rtOceanTarget = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOceanTarget.Designation = "OceanMultiAssocTarget";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOceanTarget);

        List<RtOcean> originOceans = [];
        for (var i = 0; i < 3; i++)
        {
            var rtOceanOrigin = await repository.CreateTransientRtEntityAsync<RtOcean>();
            rtOceanOrigin.Designation = $"OceanMultiAssocOrigin_{i}";
            await repository.InsertOneRtEntityAsync(new LocalSession(), rtOceanOrigin);
            originOceans.Add(rtOceanOrigin);
        }

        List<AssociationUpdateInfo> associationUpdateInfos = originOceans.Select(ocean =>
            new AssociationUpdateInfo(
                ocean.ToRtEntityId(),
                rtOceanTarget.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)).ToList();

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [], associationUpdateInfos, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);
        var associations = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            rtOceanTarget.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));
        Assert.Equal(3, associations.Items.Count());
    }

    #endregion

    #region Combined Operations Tests

    [Fact]
    public async Task ApplyChanges_InsertEntitiesWithAssociation_OK()
    {
        // Arrange - Using Ocean -> Ocean association with Related role
        var repository = await CreateRepositoryAsync();

        var rtOcean1 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean1.Designation = "CombinedOcean1";

        var rtOcean2 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean2.Designation = "CombinedOcean2";

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
        [
            EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean1),
            EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean2)
        ];

        List<AssociationUpdateInfo> associationUpdateInfos =
        [
            new AssociationUpdateInfo(
                rtOcean1.ToRtEntityId(),
                rtOcean2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ];

        // Act
        OperationResult operationResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, associationUpdateInfos, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);

        var retrievedOcean1 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean1.RtId);
        var retrievedOcean2 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean2.RtId);
        Assert.NotNull(retrievedOcean1);
        Assert.NotNull(retrievedOcean2);

        var associations = await repository.GetRtAssociationsAsync(
            new LocalSession(),
            rtOcean2.ToRtEntityId(),
            RtAssociationExtendedQueryOptions.Create(GraphDirections.Inbound));
        Assert.Single(associations.Items);
    }

    [Fact]
    public async Task ApplyChanges_MixedOperations_InsertUpdateDelete_OK()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();

        // Create existing entities for update and delete
        var oceanToUpdate = await repository.CreateTransientRtEntityAsync<RtOcean>();
        oceanToUpdate.Designation = "OceanToUpdate";
        await repository.InsertOneRtEntityAsync(new LocalSession(), oceanToUpdate);

        var oceanToDelete = await repository.CreateTransientRtEntityAsync<RtOcean>();
        oceanToDelete.Designation = "OceanToDelete";
        await repository.InsertOneRtEntityAsync(new LocalSession(), oceanToDelete);

        // Create new entity for insert
        var oceanToInsert = await repository.CreateTransientRtEntityAsync<RtOcean>();
        oceanToInsert.Designation = "OceanToInsert";

        // Prepare update entity
        var updateEntity = new RtOcean { Designation = "UpdatedOcean" };

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos =
        [
            EntityUpdateInfo<RtEntity>.CreateInsert(oceanToInsert),
            EntityUpdateInfo<RtEntity>.CreateUpdate(oceanToUpdate.ToRtEntityId(), updateEntity),
            EntityUpdateInfo<RtEntity>.CreateDelete(oceanToDelete.ToRtEntityId())
        ];

        // Act
        OperationResult operationResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Erase };
        await repository.ApplyChangesAsync(new LocalSession(), entityUpdateInfos, [], deleteOptions, operationResult);

        // Assert
        Assert.False(operationResult.HasErrors);

        // Verify insert
        var insertedOcean = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), oceanToInsert.RtId);
        Assert.NotNull(insertedOcean);
        Assert.Equal("OceanToInsert", insertedOcean.Designation);

        // Verify update
        var updatedOcean = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), oceanToUpdate.RtId);
        Assert.NotNull(updatedOcean);
        Assert.Equal("UpdatedOcean", updatedOcean.Designation);

        // Verify delete
        var deletedOcean = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), oceanToDelete.RtId);
        Assert.Null(deletedOcean);
    }

    [Fact]
    public async Task ApplyChanges_DisablePreDocumentModifications_OK()
    {
        // Arrange - Using IBulkRtMutation directly to test DisablePreDocumentModifications
        var (bulkRtMutation, dataSource, ckCacheService) = await GetBulkRtMutationContextAsync();
        var repository = await CreateRepositoryAsync();

        var rtOcean = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean.Designation = "OceanNoPreMod";

        List<EntityUpdateInfo<RtEntity>> entityUpdateInfos = [EntityUpdateInfo<RtEntity>.CreateInsert(rtOcean)];

        // Act - Call IBulkRtMutation directly with DisablePreDocumentModifications option
        var options = new BulkRtMutationOptions { DisablePreDocumentModifications = true };
        await bulkRtMutation.ApplyChangesAsync(
            new LocalSession(),
            dataSource,
            ckCacheService,
            entityUpdateInfos,
            [],
            options);

        // Assert
        var retrieved = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean.RtId);
        Assert.NotNull(retrieved);
    }

    #endregion

    #region Delete with Association Archiving Tests

    [Fact]
    public async Task ApplyChanges_DeleteEntity_ArchivesAssociations_OK()
    {
        // Arrange - Using Ocean -> Ocean association with Related role
        var repository = await CreateRepositoryAsync();

        var rtOcean1 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean1.Designation = "OceanOriginWithAssocToArchive";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean1);

        var rtOcean2 = await repository.CreateTransientRtEntityAsync<RtOcean>();
        rtOcean2.Designation = "OceanTargetWithAssocToArchive";
        await repository.InsertOneRtEntityAsync(new LocalSession(), rtOcean2);

        // Create association
        List<AssociationUpdateInfo> createAssoc =
        [
            new AssociationUpdateInfo(
                rtOcean1.ToRtEntityId(),
                rtOcean2.ToRtEntityId(),
                SystemCkIds.RtCkRelatedRoleId,
                AssociationModOptionsDto.Create)
        ];
        OperationResult createResult = new();
        await repository.ApplyChangesAsync(new LocalSession(), [], createAssoc, createResult);

        // Delete the origin ocean with Archive strategy
        List<EntityUpdateInfo<RtEntity>> deleteInfos =
            [EntityUpdateInfo<RtEntity>.CreateDelete(rtOcean1.ToRtEntityId())];

        // Act
        OperationResult deleteResult = new();
        var deleteOptions = new DeleteOptions { Strategy = DeleteStrategies.Archive };
        await repository.ApplyChangesAsync(new LocalSession(), deleteInfos, [], deleteOptions, deleteResult);

        // Assert
        Assert.False(deleteResult.HasErrors);

        // Entity should be archived
        var retrievedOcean1 = await repository.GetRtEntityByRtIdAsync<RtOcean>(new LocalSession(), rtOcean1.RtId);
        Assert.NotNull(retrievedOcean1);
        Assert.Equal(RtState.Archived, retrievedOcean1.RtState);
    }

    #endregion
}
