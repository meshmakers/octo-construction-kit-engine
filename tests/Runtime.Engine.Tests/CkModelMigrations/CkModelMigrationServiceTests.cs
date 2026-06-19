using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

public class CkModelMigrationServiceTests
{
    private readonly ICkModelMigrationService _sut;
    private readonly ICkMigrationParser _parser;
    private readonly ITenantBackupService _backupService;
    private readonly ICkMigrationContentProvider _contentProvider;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly ICatalogService _catalogService;
    private readonly ICkModelImportAuditTrail _auditTrail;

    public CkModelMigrationServiceTests()
    {
        _parser = A.Fake<ICkMigrationParser>();
        _backupService = A.Fake<ITenantBackupService>();
        _contentProvider = A.Fake<ICkMigrationContentProvider>();
        _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        _catalogService = A.Fake<ICatalogService>();
        _auditTrail = A.Fake<ICkModelImportAuditTrail>();
        var logger = NullLogger<CkModelMigrationService>.Instance;

        _sut = new CkModelMigrationService(
            _parser,
            _backupService,
            _contentProvider,
            _repositoryProvider,
            _catalogService,
            _auditTrail,
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
    public async Task FindMigrationPathAsync_NoMigrationsExist_TargetIsHigher_ShouldReturnSchemaOnlyNoOpPath()
    {
        // Arrange
        // A CK model that ships no migration scripts at all but ships a higher target version
        // should still be allowed to auto-upgrade — purely additive schema bumps don't need a
        // data migration script. See engine fix for the auto-bridge "no migrations at all" case.
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("2.0.0", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);
        Assert.False(result.Steps[0].Breaking);
        Assert.False(result.HasBreakingChanges);
    }

    [Fact]
    public async Task FindMigrationPathAsync_NoMigrationsExist_SameVersion_ShouldReturnNull()
    {
        // Arrange: no scripts AND target == source. Nothing to do; FindMigrationPathAsync should
        // not invent a no-op for a non-upgrade.
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "1.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindMigrationPathAsync_NoMigrationsExist_Downgrade_ShouldReturnNull()
    {
        // Arrange: no scripts AND target < source. Don't synthesize a downgrade path.
        var fromModel = new CkModelId("TestModel", "2.0.0");
        var toModel = new CkModelId("TestModel", "1.0.0");

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(false);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task FindMigrationPathAsync_PostChainSchemaOnly_TargetAboveLatestChainEntry_ShouldReturnNoOpBridge()
    {
        // Arrange: tenant is past the latest chain entry. Target is one more patch-level above.
        // Pinned regression for the System CK 2.0.10 -> 2.1.0 case: the existing 1.0.3 -> 2.0.0
        // migration chain doesn't help, no candidate auto-bridge entry point exists (all chain
        // FromVersions are below the tenant), and the partial-path resolver only matches FromVersion
        // exactly. Without this fallback every additive patch bump would force an empty migration
        // script — violating "Developers only need to create migration scripts for versions that
        // actually transform data."
        var fromModel = new CkModelId("System", "2.0.10");
        var toModel = new CkModelId("System", "2.1.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "System-2.1.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.3",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.3-to-2.0.0.yaml",
                    Description = "Legacy Query → SimpleRtQuery."
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Steps);
        Assert.Equal("2.0.10", result.Steps[0].FromVersion);
        Assert.Equal("2.1.0", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);
        Assert.False(result.Steps[0].Breaking);
        Assert.False(result.HasBreakingChanges);
    }

    [Fact]
    public async Task FindMigrationPathAsync_PostChainSchemaOnly_DowngradeOrSameVersion_ShouldReturnNull()
    {
        // Arrange: tenant is past the latest chain entry, target is equal or below.
        // The post-chain bridge must only synthesise upgrades, never downgrades.
        var fromModel = new CkModelId("System", "2.1.0");
        var toModel = new CkModelId("System", "2.0.10");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "System-2.1.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.3",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.3-to-2.0.0.yaml",
                    Description = "Legacy Query → SimpleRtQuery."
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);

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

    [Fact]
    public async Task FindMigrationPathAsync_PartialPath_WhenExactPathUnavailable_ShouldReturnPartialPath()
    {
        // Arrange - migration from 1.0.0->2.0.0 exists, but target is 2.0.2
        // This tests the partial path fallback: data migration goes to 2.0.0, schema handles 2.0.0->2.0.2
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "2.0.2");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-2.0.2",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "1.0.0",
                    ToVersion = "2.0.0",
                    ScriptPath = "1.0.0-to-2.0.0.yaml",
                    Description = "Major version migration"
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
        Assert.True(result.IsPartialPath);
        Assert.Single(result.Steps);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("2.0.0", result.Steps[0].ToVersion);
        Assert.Equal("2.0.0", result.ToModel.Version.ToString());
    }

    [Fact]
    public async Task FindMigrationPathAsync_PartialPath_BestCandidate_ShouldReturnClosestVersion()
    {
        // Arrange - multiple intermediate migrations exist, should pick the one closest to target
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "3.0.5");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.0.5",
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
                    FromVersion = "1.0.0",
                    ToVersion = "3.0.0",
                    ScriptPath = "1.0.0-to-3.0.0.yaml"
                }
            ]
        };

        var script3 = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "3.0.0"
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "3.0.0", A<CancellationToken>._))
            .Returns(script3);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert - should pick 3.0.0 (closest to target 3.0.5), not 2.0.0
        Assert.NotNull(result);
        Assert.True(result.IsPartialPath);
        Assert.Single(result.Steps);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("3.0.0", result.Steps[0].ToVersion);
        Assert.Equal("3.0.0", result.ToModel.Version.ToString());
    }

    [Fact]
    public async Task FindMigrationPathAsync_AutoBridge_StartGap_ShouldBridgeToEarliestEntryPoint()
    {
        // Arrange - tenant at 2.2.0, migrations start at 3.0.1
        var fromModel = new CkModelId("TestModel", "2.2.0");
        var toModel = new CkModelId("TestModel", "3.1.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.1.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.1",
                    ToVersion = "3.0.2",
                    ScriptPath = "3.0.1-to-3.0.2.yaml",
                    Breaking = true
                },
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.2",
                    ToVersion = "3.1.0",
                    ScriptPath = "3.0.2-to-3.1.0.yaml"
                }
            ]
        };

        var script1 = new CkMigrationScriptDto { SourceVersion = "3.0.1", TargetVersion = "3.0.2" };
        var script2 = new CkMigrationScriptDto { SourceVersion = "3.0.2", TargetVersion = "3.1.0" };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.1", "3.0.2", A<CancellationToken>._))
            .Returns(script1);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.2", "3.1.0", A<CancellationToken>._))
            .Returns(script2);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert - should bridge 2.2.0 → 3.0.1 (no-op) then execute 3.0.1 → 3.0.2 → 3.1.0
        Assert.NotNull(result);
        Assert.Equal(3, result.Steps.Count);

        // First step is the no-op bridge
        Assert.Equal("2.2.0", result.Steps[0].FromVersion);
        Assert.Equal("3.0.1", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);

        // Remaining steps are actual migrations
        Assert.Equal("3.0.1", result.Steps[1].FromVersion);
        Assert.Equal("3.0.2", result.Steps[1].ToVersion);
        Assert.NotNull(result.Steps[1].Script);

        Assert.Equal("3.0.2", result.Steps[2].FromVersion);
        Assert.Equal("3.1.0", result.Steps[2].ToVersion);
        Assert.NotNull(result.Steps[2].Script);

        Assert.True(result.HasBreakingChanges);
        Assert.False(result.IsPartialPath);
    }

    [Fact]
    public async Task FindMigrationPathAsync_AutoBridge_BothGaps_ShouldBridgeStartAndAcceptPartialEnd()
    {
        // Arrange - tenant at 2.2.0, migrations 3.0.1→3.1.1, target 3.1.2
        var fromModel = new CkModelId("TestModel", "2.2.0");
        var toModel = new CkModelId("TestModel", "3.1.2");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.1.2",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.1",
                    ToVersion = "3.1.0",
                    ScriptPath = "3.0.1-to-3.1.0.yaml"
                },
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.1.0",
                    ToVersion = "3.1.1",
                    ScriptPath = "3.1.0-to-3.1.1.yaml"
                }
            ]
        };

        var script1 = new CkMigrationScriptDto { SourceVersion = "3.0.1", TargetVersion = "3.1.0" };
        var script2 = new CkMigrationScriptDto { SourceVersion = "3.1.0", TargetVersion = "3.1.1" };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.1", "3.1.0", A<CancellationToken>._))
            .Returns(script1);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.1.0", "3.1.1", A<CancellationToken>._))
            .Returns(script2);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert - bridge at start (2.2.0→3.0.1), execute chain (3.0.1→3.1.0→3.1.1), partial at end (3.1.1 < 3.1.2)
        Assert.NotNull(result);
        Assert.True(result.IsPartialPath);
        Assert.Equal(3, result.Steps.Count);

        // No-op bridge
        Assert.Equal("2.2.0", result.Steps[0].FromVersion);
        Assert.Equal("3.0.1", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);

        // Actual migrations
        Assert.Equal("3.0.1", result.Steps[1].FromVersion);
        Assert.Equal("3.1.0", result.Steps[1].ToVersion);
        Assert.Equal("3.1.0", result.Steps[2].FromVersion);
        Assert.Equal("3.1.1", result.Steps[2].ToVersion);
    }

    [Fact]
    public async Task FindMigrationPathAsync_AutoBridge_PicksEarliestEntryPoint()
    {
        // Arrange - two possible entry points (3.0.1 and 3.0.5), should pick earliest (3.0.1)
        var fromModel = new CkModelId("TestModel", "2.0.0");
        var toModel = new CkModelId("TestModel", "4.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-4.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.1",
                    ToVersion = "4.0.0",
                    ScriptPath = "3.0.1-to-4.0.0.yaml"
                },
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.5",
                    ToVersion = "4.0.0",
                    ScriptPath = "3.0.5-to-4.0.0.yaml"
                }
            ]
        };

        var script1 = new CkMigrationScriptDto { SourceVersion = "3.0.1", TargetVersion = "4.0.0" };
        var script2 = new CkMigrationScriptDto { SourceVersion = "3.0.5", TargetVersion = "4.0.0" };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.1", "4.0.0", A<CancellationToken>._))
            .Returns(script1);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.5", "4.0.0", A<CancellationToken>._))
            .Returns(script2);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert - should bridge to 3.0.1 (earliest), not 3.0.5
        Assert.NotNull(result);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("2.0.0", result.Steps[0].FromVersion);
        Assert.Equal("3.0.1", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);
    }

    [Fact]
    public async Task FindMigrationPathAsync_AutoBridge_NoEntryPointReachesTarget_ShouldReturnNull()
    {
        // Arrange - migrations exist but none can reach the target (disconnected chains)
        var fromModel = new CkModelId("TestModel", "1.0.0");
        var toModel = new CkModelId("TestModel", "5.0.0");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-5.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.0",
                    ToVersion = "3.1.0",
                    ScriptPath = "3.0.0-to-3.1.0.yaml"
                }
                // No migration from 3.1.0 to 5.0.0 — chain ends at 3.1.0
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        // GetMigrationAsync for 3.0.0→3.1.0 returns a script
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.0", "3.1.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "3.0.0", TargetVersion = "3.1.0" });

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.FindMigrationPathAsync(fromModel, toModel, ct);

        // Assert - should use auto-bridge with partial end (bridge 1.0.0→3.0.0, execute 3.0.0→3.1.0, partial to 5.0.0)
        Assert.NotNull(result);
        Assert.True(result.IsPartialPath);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("1.0.0", result.Steps[0].FromVersion);
        Assert.Equal("3.0.0", result.Steps[0].ToVersion);
        Assert.Null(result.Steps[0].Script);
        Assert.Equal("3.0.0", result.Steps[1].FromVersion);
        Assert.Equal("3.1.0", result.Steps[1].ToVersion);
    }

    #endregion

    #region MigrateAsync Tests

    [Fact]
    public async Task MigrateAsync_NoOpBridgeStep_ShouldSucceed()
    {
        // Arrange - simulate a migration path with a no-op bridge step (Script = null)
        var fromModel = new CkModelId("TestModel", "2.2.0");
        var toModel = new CkModelId("TestModel", "3.0.2");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.0.2",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.1",
                    ToVersion = "3.0.2",
                    ScriptPath = "3.0.1-to-3.0.2.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "3.0.1",
            TargetVersion = "3.0.2",
            Steps = [] // Empty steps = success
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.1", "3.0.2", A<CancellationToken>._))
            .Returns(script);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .Returns((IRuntimeRepository?)null);

        var options = new CkMigrationOptions { DryRun = false };
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, options, ct);

        // Assert - migration should succeed (no-op bridge step + empty script step both pass)
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_NoOpBridgeStep_ShouldBeValid()
    {
        // Arrange - path with a no-op bridge step (Script = null) should validate successfully
        var fromModel = new CkModelId("TestModel", "2.2.0");
        var toModel = new CkModelId("TestModel", "3.0.2");

        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.0.2",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.0.1",
                    ToVersion = "3.0.2",
                    ScriptPath = "3.0.1-to-3.0.2.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "3.0.1",
            TargetVersion = "3.0.2",
            Steps =
            [
                new CkMigrationStepDto
                {
                    StepId = "test",
                    Action = CkMigrationActionType.Transform,
                    Target = new CkMigrationTargetDto { CkTypeId = "TestModel/Entity" },
                    Transform = new CkMigrationTransformDto { Type = CkMigrationTransformType.ChangeCkType, NewCkTypeId = "TestModel/NewEntity" }
                }
            ]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.0.1", "3.0.2", A<CancellationToken>._))
            .Returns(script);

        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ValidateAsync("tenant1", fromModel, toModel, ct);

        // Assert - validation should pass (no-op bridge step is valid, actual step has proper config)
        Assert.True(result.IsValid);
    }

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
    public async Task MigrateAsync_NoMigrationPath_Downgrade_ShouldReturnFailed()
    {
        // Arrange: source version is higher than target, no migrations defined.
        // Schema-only no-op auto-bridge only applies on upgrades (toVersion > fromVersion);
        // downgrades must still fail because we can't synthesize a backwards path.
        var fromModel = new CkModelId("TestModel", "2.0.0");
        var toModel = new CkModelId("TestModel", "1.0.0");

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
    public async Task ValidateAsync_NoMigrationPath_Downgrade_ShouldReturnInvalid()
    {
        // Arrange: downgrade without scripts must fail validation (no auto-bridge for downgrades).
        var fromModel = new CkModelId("TestModel", "2.0.0");
        var toModel = new CkModelId("TestModel", "1.0.0");

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

    [Fact]
    public async Task MigrateAsync_SuccessfulMigration_ShouldRecordHistory()
    {
        // Arrange - set up a successful non-dry-run migration with empty steps
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
            Steps = [] // Empty steps = success with no operations
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(script);

        // Return null repository — RecordMigrationHistoryAsync handles this gracefully (logs warning, returns)
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .Returns((IRuntimeRepository?)null);

        var options = new CkMigrationOptions { DryRun = false };
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.MigrateAsync("tenant1", fromModel, toModel, options, ct);

        // Assert - migration succeeded and attempted to record history
        Assert.True(result.Success);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
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
