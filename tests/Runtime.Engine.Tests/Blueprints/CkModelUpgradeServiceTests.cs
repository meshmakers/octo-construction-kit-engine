using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for <see cref="CkModelUpgradeService"/>
/// </summary>
public class CkModelUpgradeServiceTests
{
    private readonly ICkModelMigrationService _migrationService;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ICkModelUpgradeService _upgradeService;

    public CkModelUpgradeServiceTests()
    {
        _migrationService = A.Fake<ICkModelMigrationService>();
        _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        var logger = NullLogger<CkModelUpgradeService>.Instance;

        _upgradeService = new CkModelUpgradeService(
            _migrationService,
            _repositoryProvider,
            logger);
    }

    #region UpgradeModelsAsync Tests

    [Fact]
    public async Task UpgradeModelsAsync_FirstInstallation_ShouldSkipMigration()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "1.0.0")
        };

        // No repository available = no existing versions
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(tenantId, ct))
            .Returns((IRuntimeRepository?)null);

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.SkippedModels);
        Assert.Empty(result.UpgradedModels);
        Assert.Empty(result.FailedModels);
        Assert.Equal("MyModel", result.SkippedModels[0].CkModelName);
        Assert.False(result.SkippedModels[0].UpgradeNeeded);
    }

    [Fact]
    public async Task UpgradeModelsAsync_AlreadyAtTargetVersion_ShouldSkip()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "1.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.SkippedModels);
        Assert.Empty(result.UpgradedModels);
        Assert.Empty(result.FailedModels);
        Assert.Equal("MyModel", result.SkippedModels[0].CkModelName);
        Assert.Equal("1.0.0", result.SkippedModels[0].InstalledVersion);
        Assert.False(result.SkippedModels[0].UpgradeNeeded);
    }

    [Fact]
    public async Task UpgradeModelsAsync_MigrationNeeded_ShouldExecuteMigration()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "2.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "2.0.0");

        // Setup migration path exists
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModel, toModel, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModel,
                ToModel = toModel,
                HasBreakingChanges = false,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        // Setup migration succeeds
        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModel, toModel, A<CkMigrationOptions>._, ct))
            .Returns(new CkMigrationResult
            {
                Success = true,
                FromModel = fromModel,
                ToModel = toModel,
                EntitiesUpdated = 10
            });

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.UpgradedModels);
        Assert.Empty(result.FailedModels);
        Assert.Equal("MyModel", result.UpgradedModels[0].CkModelName);
        Assert.Equal("1.0.0", result.UpgradedModels[0].InstalledVersion);
        Assert.Equal("2.0.0", result.UpgradedModels[0].TargetVersion);
        Assert.True(result.UpgradedModels[0].UpgradeNeeded);
        Assert.True(result.UpgradedModels[0].MigrationPathAvailable);
        Assert.Equal(10, result.TotalEntitiesAffected);

        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModel, toModel, A<CkMigrationOptions>._, ct))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpgradeModelsAsync_NoMigrationPath_ShouldRecordVersionAndWarn()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "2.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "2.0.0");

        // Setup: no migration path
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModel, toModel, ct))
            .Returns((CkMigrationPath?)null);

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success); // Still success, just with warning
        Assert.Single(result.SkippedModels);
        Assert.Empty(result.UpgradedModels);
        Assert.Single(result.Warnings);
        Assert.Contains("No migration path", result.SkippedModels[0].ErrorMessage);

        // Migration should NOT be called
        A.CallTo(() => _migrationService.MigrateAsync(A<string>._, A<CkModelId>._, A<CkModelId>._, A<CkMigrationOptions>._, ct))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task UpgradeModelsAsync_MigrationFails_ShouldReportFailure()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "2.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "2.0.0");

        // Setup migration path exists
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModel, toModel, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModel,
                ToModel = toModel,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        // Setup migration fails
        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModel, toModel, A<CkMigrationOptions>._, ct))
            .Returns(new CkMigrationResult
            {
                Success = false,
                FromModel = fromModel,
                ToModel = toModel,
                Errors = ["Migration step failed"]
            });

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.False(result.Success);
        Assert.Single(result.FailedModels);
        Assert.Empty(result.UpgradedModels);
        Assert.Single(result.Errors);
        Assert.Contains("Migration step failed", result.Errors[0]);
    }

    [Fact]
    public async Task UpgradeModelsAsync_MultipleModels_ShouldProcessAll()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("ModelA", "2.0.0"),
            new("ModelB", "1.0.0"),
            new("ModelC", "3.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["ModelA"] = "1.0.0", // Needs upgrade
            ["ModelB"] = "1.0.0"  // Already at version
            // ModelC not installed - first installation
        }, ct);

        var fromModelA = new CkModelId("ModelA", "1.0.0");
        var toModelA = new CkModelId("ModelA", "2.0.0");

        // Setup migration path for ModelA
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModelA, toModelA, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModelA,
                ToModel = toModelA,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModelA, toModelA, A<CkMigrationOptions>._, ct))
            .Returns(new CkMigrationResult
            {
                Success = true,
                FromModel = fromModelA,
                ToModel = toModelA,
                EntitiesUpdated = 5
            });

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.UpgradedModels); // ModelA
        Assert.Equal(2, result.SkippedModels.Count); // ModelB (at version) + ModelC (first install)
        Assert.Empty(result.FailedModels);
    }

    [Fact]
    public async Task UpgradeModelsAsync_WithBreakingChanges_ShouldReportBreaking()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("MyModel", "3.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "3.0.0");

        // Setup migration path with breaking changes
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModel, toModel, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModel,
                ToModel = toModel,
                HasBreakingChanges = true,
                Steps = [
                    new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0", Breaking = false },
                    new CkMigrationStep { FromVersion = "2.0.0", ToVersion = "3.0.0", Breaking = true }
                ]
            });

        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModel, toModel, A<CkMigrationOptions>._, ct))
            .Returns(CkMigrationResult.Succeeded(fromModel, toModel));

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, null, null, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.UpgradedModels);
        Assert.True(result.UpgradedModels[0].HasBreakingChanges);
    }

    [Fact]
    public async Task UpgradeModelsAsync_ContinueOnError_ShouldProcessRemainingModels()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";
        var modelIds = new List<CkModelIdVersionRange>
        {
            new("ModelA", "2.0.0"),
            new("ModelB", "2.0.0")
        };

        var repository = SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["ModelA"] = "1.0.0",
            ["ModelB"] = "1.0.0"
        }, ct);

        var fromModelA = new CkModelId("ModelA", "1.0.0");
        var toModelA = new CkModelId("ModelA", "2.0.0");
        var fromModelB = new CkModelId("ModelB", "1.0.0");
        var toModelB = new CkModelId("ModelB", "2.0.0");

        // Setup migration paths
        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModelA, toModelA, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModelA,
                ToModel = toModelA,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModelB, toModelB, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModelB,
                ToModel = toModelB,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        // ModelA fails, ModelB succeeds
        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModelA, toModelA, A<CkMigrationOptions>._, ct))
            .Returns(CkMigrationResult.Failed(fromModelA, toModelA, "ModelA migration failed"));

        A.CallTo(() => _migrationService.MigrateAsync(tenantId, fromModelB, toModelB, A<CkMigrationOptions>._, ct))
            .Returns(CkMigrationResult.Succeeded(fromModelB, toModelB));

        var options = new CkMigrationOptions { ContinueOnError = true };

        // Act
        var result = await _upgradeService.UpgradeModelsAsync(tenantId, modelIds, options, null, ct);

        // Assert
        Assert.False(result.Success); // Overall failure due to ModelA
        Assert.Single(result.FailedModels); // ModelA
        Assert.Single(result.UpgradedModels); // ModelB was still processed
    }

    #endregion

    #region CheckUpgradeNeededAsync Tests

    [Fact]
    public async Task CheckUpgradeNeededAsync_NoRepository_ShouldReturnNotNeeded()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";

        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(tenantId, ct))
            .Returns((IRuntimeRepository?)null);

        // Act
        var result = await _upgradeService.CheckUpgradeNeededAsync(tenantId, "MyModel", "1.0.0", ct);

        // Assert
        Assert.Equal("MyModel", result.CkModelName);
        Assert.Equal("1.0.0", result.TargetVersion);
        Assert.False(result.UpgradeNeeded);
        Assert.Null(result.InstalledVersion);
    }

    [Fact]
    public async Task CheckUpgradeNeededAsync_SameVersion_ShouldReturnNotNeeded()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";

        SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        // Act
        var result = await _upgradeService.CheckUpgradeNeededAsync(tenantId, "MyModel", "1.0.0", ct);

        // Assert
        Assert.False(result.UpgradeNeeded);
        Assert.Equal("1.0.0", result.InstalledVersion);
    }

    [Fact]
    public async Task CheckUpgradeNeededAsync_DifferentVersion_ShouldReturnNeeded()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";

        SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["MyModel"] = "1.0.0"
        }, ct);

        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "2.0.0");

        A.CallTo(() => _migrationService.FindMigrationPathAsync(fromModel, toModel, ct))
            .Returns(new CkMigrationPath
            {
                FromModel = fromModel,
                ToModel = toModel,
                HasBreakingChanges = true,
                Steps = [new CkMigrationStep { FromVersion = "1.0.0", ToVersion = "2.0.0" }]
            });

        // Act
        var result = await _upgradeService.CheckUpgradeNeededAsync(tenantId, "MyModel", "2.0.0", ct);

        // Assert
        Assert.True(result.UpgradeNeeded);
        Assert.Equal("1.0.0", result.InstalledVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
        Assert.True(result.MigrationPathAvailable);
        Assert.True(result.HasBreakingChanges);
    }

    #endregion

    #region GetInstalledVersionsAsync Tests

    [Fact]
    public async Task GetInstalledVersionsAsync_NoRepository_ShouldReturnEmpty()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";

        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(tenantId, ct))
            .Returns((IRuntimeRepository?)null);

        // Act
        var result = await _upgradeService.GetInstalledVersionsAsync(tenantId, ct);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetInstalledVersionsAsync_WithHistory_ShouldReturnLatestVersions()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var tenantId = "tenant1";

        // Setup history with multiple entries (simulating upgrades)
        SetupRepositoryWithHistory(tenantId, new Dictionary<string, string>
        {
            ["ModelA"] = "2.0.0",
            ["ModelB"] = "1.5.0"
        }, ct);

        // Act
        var result = await _upgradeService.GetInstalledVersionsAsync(tenantId, ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("2.0.0", result["ModelA"]);
        Assert.Equal("1.5.0", result["ModelB"]);
    }

    #endregion

    #region Helper Methods

    private IRuntimeRepository SetupRepositoryWithHistory(
        string tenantId,
        Dictionary<string, string> modelVersions,
        CancellationToken ct)
    {
        var repository = A.Fake<IRuntimeRepository>();
        var session = A.Fake<IOctoSession>();

        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(tenantId, ct))
            .Returns(repository);
        A.CallTo(() => repository.GetSessionAsync())
            .Returns(Task.FromResult(session));

        // Create entities for each model version
        var entities = new List<RtEntity>();
        var executedAt = DateTime.UtcNow;

        foreach (var kvp in modelVersions)
        {
            var entity = CreateHistoryEntity(kvp.Key, kvp.Value, executedAt);
            entities.Add(entity);
            executedAt = executedAt.AddSeconds(-1); // Earlier time for older entries
        }

        var resultSet = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => resultSet.Items).Returns(entities);

        A.CallTo(() => repository.GetRtEntitiesByTypeAsync(
            session,
            A<RtCkId<CkTypeId>>._,
            A<RtEntityQueryOptions>._))
            .Returns(resultSet);

        // For recording new versions
        A.CallTo(() => repository.CreateTransientRtEntityByRtCkIdAsync(
            A<RtCkId<CkTypeId>>._))
            .Returns(Task.FromResult(new RtEntity()));

        return repository;
    }

    private static RtEntity CreateHistoryEntity(string modelName, string toVersion, DateTime executedAt)
    {
        var entity = new RtEntity();
        entity.SetAttributeValue("CkModelName", AttributeValueTypesDto.String, modelName);
        entity.SetAttributeValue("FromVersion", AttributeValueTypesDto.String, toVersion);
        entity.SetAttributeValue("ToVersion", AttributeValueTypesDto.String, toVersion);
        entity.SetAttributeValue("ExecutedAt", AttributeValueTypesDto.DateTime, executedAt);
        entity.SetAttributeValue("Success", AttributeValueTypesDto.Boolean, true);
        return entity;
    }

    #endregion
}
