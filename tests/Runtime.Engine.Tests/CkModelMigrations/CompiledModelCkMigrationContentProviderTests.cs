using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

public class CompiledModelCkMigrationContentProviderTests
{
    private readonly CompiledModelCkMigrationContentProvider _sut;

    public CompiledModelCkMigrationContentProviderTests()
    {
        var logger = A.Fake<ILogger<CompiledModelCkMigrationContentProvider>>();
        _sut = new CompiledModelCkMigrationContentProvider(logger);
    }

    [Fact]
    public async Task HasMigrationsAsync_NoData_ReturnsFalse()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasMigrationsAsync_WithData_ReturnsTrue()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = CreateMigrationData("1.0.0", "2.0.0");
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetMigrationMetaAsync_ReturnsCorrectMeta()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = CreateMigrationData("1.0.0", "2.0.0");
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel-1.0.0", result.CkModelId);
    }

    [Fact]
    public async Task GetMigrationMetaAsync_NoData_ReturnsNull()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMigrationsAsync_ReturnsAllScripts()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = new CkCompiledMigrationDataDto
        {
            Meta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" },
            Scripts =
            [
                new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" },
                new CkMigrationScriptDto { SourceVersion = "2.0.0", TargetVersion = "3.0.0" }
            ]
        };
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("1.0.0", result[0].SourceVersion);
        Assert.Equal("2.0.0", result[0].TargetVersion);
        Assert.Equal("2.0.0", result[1].SourceVersion);
        Assert.Equal("3.0.0", result[1].TargetVersion);
    }

    [Fact]
    public async Task GetMigrationsAsync_NoData_ReturnsEmptyList()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMigrationAsync_ReturnsSpecificScript()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = new CkCompiledMigrationDataDto
        {
            Meta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" },
            Scripts =
            [
                new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" },
                new CkMigrationScriptDto { SourceVersion = "2.0.0", TargetVersion = "3.0.0" }
            ]
        };
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationAsync(ckModelId, "2.0.0", "3.0.0", ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.SourceVersion);
        Assert.Equal("3.0.0", result.TargetVersion);
    }

    [Fact]
    public async Task GetMigrationAsync_NoMatch_ReturnsNull()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = CreateMigrationData("1.0.0", "2.0.0");
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.GetMigrationAsync(ckModelId, "3.0.0", "4.0.0", ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearMigrationData_RemovesDataForModel()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationData = CreateMigrationData("1.0.0", "2.0.0");
        _sut.SetMigrationData(ckModelId, migrationData);
        var ct = TestContext.Current.CancellationToken;

        // Act
        _sut.ClearMigrationData(ckModelId);

        // Assert
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);
        Assert.False(result);
    }

    [Fact]
    public async Task MultipleModels_IndependentStorage()
    {
        // Arrange
        var modelA = new CkModelId("ModelA", "1.0.0");
        var modelB = new CkModelId("ModelB", "1.0.0");
        var dataA = CreateMigrationData("1.0.0", "2.0.0");
        var dataB = CreateMigrationData("2.0.0", "3.0.0");
        _sut.SetMigrationData(modelA, dataA);
        _sut.SetMigrationData(modelB, dataB);
        var ct = TestContext.Current.CancellationToken;

        // Act - clear only model A
        _sut.ClearMigrationData(modelA);

        // Assert - model A cleared, model B still has data
        Assert.False(await _sut.HasMigrationsAsync(modelA, ct));
        Assert.True(await _sut.HasMigrationsAsync(modelB, ct));

        var scriptsB = await _sut.GetMigrationsAsync(modelB, ct);
        Assert.Single(scriptsB);
        Assert.Equal("2.0.0", scriptsB[0].SourceVersion);
    }

    private static CkCompiledMigrationDataDto CreateMigrationData(string sourceVersion, string targetVersion)
    {
        return new CkCompiledMigrationDataDto
        {
            Meta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" },
            Scripts =
            [
                new CkMigrationScriptDto
                {
                    SourceVersion = sourceVersion,
                    TargetVersion = targetVersion
                }
            ]
        };
    }
}
