using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

public class CkModelMigrationServiceTests
{
    private readonly ICkModelMigrationService _sut;
    private readonly ICkMigrationParser _parser;
    private readonly ITenantBackupService _backupService;
    private readonly ICkMigrationContentProvider _contentProvider;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ICatalogService _catalogService;

    public CkModelMigrationServiceTests()
    {
        _parser = A.Fake<ICkMigrationParser>();
        _backupService = A.Fake<ITenantBackupService>();
        _contentProvider = A.Fake<ICkMigrationContentProvider>();
        _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        _catalogService = A.Fake<ICatalogService>();
        var logger = NullLogger<CkModelMigrationService>.Instance;

        _sut = new CkModelMigrationService(
            _parser,
            _backupService,
            _contentProvider,
            _repositoryProvider,
            _catalogService,
            logger);
    }

    #region FindMigrationPathAsync Tests

    [Fact]
    public async Task FindMigrationPathAsync_DifferentModelNames_ShouldReturnNull()
    {
        // Arrange
        var fromModel = new CkModelId("Model1", "1.0.0");
        var toModel = new CkModelId("Model2", "2.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindMigrationPathAsync_NoMigrationsExist_ShouldReturnNull()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindMigrationPathAsync_DirectMigrationExists_ShouldReturnPath()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml",
                    Description = "Direct migration"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0"
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("2.0.0", result.Steps[0].ToVersion);
        Assert.NotNull(result.Steps[0].Script);
    }

    [Fact]
    public async Task FindMigrationPathAsync_MultiHopPath_ShouldReturnFullPath()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "3.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml"
                },
                new CkMigrationReferenceDto
                {
                    FromVersion = "2.0.0",
                    ToVersion = "3.0.0",
                    ScriptPath = "2.0.0-to-3.0.0.yaml"
                }
            ]
        };

        var script1 = new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" };
        var script2 = new CkMigrationScriptDto { SourceVersion = "2.0.0", TargetVersion = "3.0.0" };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script1);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "2.0.0", "3.0.0", A<CancellationToken>._))
            .Returns(script2);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("2.0.0", result.Steps[0].ToVersion);
        Assert.Equal("2.0.0", result.Steps[1].FromVersion);
        Assert.Equal("3.0.0", result.Steps[1].ToVersion);
    }

    [Fact]
    public async Task FindMigrationPathAsync_BreakingChanges_ShouldSetFlag()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml",
                    Breaking = true
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" });

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasBreakingChanges);
    }

    #endregion

    #region MigrateAsync Tests

    [Fact]
    public async Task MigrateAsync_DifferentModelNames_ShouldReturnFailed()
    {
        // Arrange
        var fromModel = new CkModelId("Model1", "1.0.0");
        var toModel = new CkModelId("Model2", "2.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, cancellationToken: ct);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Cannot migrate between different CK models", result.Errors[0]);
    }

    [Fact]
    public async Task MigrateAsync_NoMigrationPath_ShouldReturnFailed()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, cancellationToken: ct);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No migration path found", result.Errors[0]);
    }

    [Fact]
    public async Task MigrateAsync_DryRun_ShouldNotExecuteChanges()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Steps =
            [
                new CkMigrationStepDto
                {
                    StepId = "test-step",
                    Action = CkMigrationActionType.Transform,
                    Target = new CkMigrationTargetDto { CkTypeId = "TestModel/Entity" },
                    Transform = new CkMigrationTransformDto { Type = CkMigrationTransformType.SetValue, TargetAttribute = "status", Value = "active" }
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var options = new CkMigrationOptions { DryRun = true };
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, options, ct);

        // Assert
        Assert.True(result.Success);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _backupService.CreateBackupAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task MigrateAsync_WithBackupOption_ShouldCreateBackup()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Steps = []
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var backupInfo = new BackupInfo { BackupId = "backup-123", TenantId = "tenant1", CreatedAt = DateTime.UtcNow };
        A.CallTo(() => _backupService.CreateBackupAsync("tenant1", A<string>._, A<CancellationToken>._))
            .Returns(backupInfo);

        var options = new CkMigrationOptions { CreateBackup = true, DryRun = false };
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, options, ct);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("backup-123", result.BackupId);
        A.CallTo(() => _backupService.CreateBackupAsync("tenant1", A<string>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_DifferentModelNames_ShouldReturnInvalid()
    {
        // Arrange
        var fromModel = new CkModelId("Model1", "1.0.0");
        var toModel = new CkModelId("Model2", "2.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Cannot migrate between different CK models"));
    }

    [Fact]
    public async Task ValidateAsync_NoMigrationPath_ShouldReturnInvalid()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("No migration path found"));
    }

    [Fact]
    public async Task ValidateAsync_ValidMigration_ShouldReturnValid()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Steps =
            [
                new CkMigrationStepDto
                {
                    StepId = "test-step",
                    Action = CkMigrationActionType.Transform,
                    Target = new CkMigrationTargetDto { CkTypeId = "TestModel/Entity" },
                    Transform = new CkMigrationTransformDto { Type = CkMigrationTransformType.SetValue, TargetAttribute = "status", Value = "active" }
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_TransformWithoutConfiguration_ShouldReturnError()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Steps =
            [
                new CkMigrationStepDto
                {
                    StepId = "bad-step",
                    Action = CkMigrationActionType.Transform,
                    Target = new CkMigrationTargetDto { CkTypeId = "TestModel/Entity" },
                    Transform = null // Missing transform configuration
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Transform configuration is required"));
    }

    [Fact]
    public async Task ValidateAsync_BreakingChanges_ShouldAddWarning()
    {
        // Arrange
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml",
                    Breaking = true
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Steps = []
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Message.Contains("breaking changes"));
    }

    #endregion

    #region GetHistoryAsync Tests

    [Fact]
    public async Task GetHistoryAsync_NoRepository_ShouldReturnEmptyList()
    {
        // Arrange
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .Returns((IRuntimeRepository?)null);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetHistoryAsync("tenant1", "TestModel", ct);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_ShouldReturnStatus()
    {
        // Arrange
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .Returns((IRuntimeRepository?)null);

        var catalogResult = new ModelListResult
        {
            TotalCount = 0,
            SkippedCount = 0,
            TakeCount = 1000,
            ModelResultItems = []
        };
        A.CallTo(() => _catalogService.ListAsync(0, 1000, A<string>._, A<CancellationToken>._))
            .Returns(catalogResult);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetStatusAsync("tenant1", "TestModel", ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel", result.CkModelName);
        Assert.Null(result.InstalledVersion);
    }

    #endregion
}
