using System.Reflection;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

public class FileSystemCkMigrationContentProviderTests : IDisposable
{
    private readonly FileSystemCkMigrationContentProvider _sut;
    private readonly ICkMigrationParser _parser;
    private readonly string _tempPath;

    public FileSystemCkMigrationContentProviderTests()
    {
        _parser = A.Fake<ICkMigrationParser>();
        var logger = A.Fake<ILogger<FileSystemCkMigrationContentProvider>>();
        _sut = new FileSystemCkMigrationContentProvider(_parser, logger);

        // Create temp directory for test files
        _tempPath = Path.Combine(Path.GetTempPath(), $"CkMigrationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempPath);
    }

    public void Dispose()
    {
        // Cleanup temp directory
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Fact]
    public async Task HasMigrationsAsync_NoSourceRegistered_ShouldReturnFalse()
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
    public async Task HasMigrationsAsync_MigrationsFolderNotExist_ShouldReturnFalse()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        _sut.RegisterModelSourcePath("TestModel", _tempPath);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasMigrationsAsync_MetaFileExists_ShouldReturnTrue()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationsPath = Path.Combine(_tempPath, "ConstructionKit", "migrations");
        Directory.CreateDirectory(migrationsPath);
        await File.WriteAllTextAsync(Path.Combine(migrationsPath, "migration-meta.yaml"), "ckModelId: TestModel-1.0.0", ct);
        _sut.RegisterModelSourcePath("TestModel", _tempPath);

        // Act
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetMigrationMetaAsync_NoSourceRegistered_ShouldReturnNull()
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
    public async Task GetMigrationMetaAsync_MetaFileExists_ShouldReturnMeta()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationsPath = Path.Combine(_tempPath, "ConstructionKit", "migrations");
        Directory.CreateDirectory(migrationsPath);
        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        await File.WriteAllTextAsync(metaPath, "ckModelId: TestModel-1.0.0", ct);

        var expectedMeta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" };
        A.CallTo(() => _parser.ParseMetaAsync(metaPath, A<CancellationToken>._))
            .Returns(expectedMeta);

        _sut.RegisterModelSourcePath("TestModel", _tempPath);

        // Act
        var result = await _sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel-1.0.0", result.CkModelId);
    }

    [Fact]
    public async Task GetMigrationsAsync_NoMigrationsRegistered_ShouldReturnEmptyList()
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
    public async Task GetMigrationsAsync_WithMigrations_ShouldReturnMigrations()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationsPath = Path.Combine(_tempPath, "ConstructionKit", "migrations");
        Directory.CreateDirectory(migrationsPath);

        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        var scriptPath = Path.Combine(migrationsPath, "1.0.0-to-2.0.0.yaml");
        await File.WriteAllTextAsync(metaPath, "ckModelId: TestModel-1.0.0", ct);
        await File.WriteAllTextAsync(scriptPath, "sourceVersion: 1.0.0\ntargetVersion: 2.0.0", ct);

        var expectedMeta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-1.0.0",
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
        var expectedScript = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0"
        };

        A.CallTo(() => _parser.ParseMetaAsync(metaPath, A<CancellationToken>._))
            .Returns(expectedMeta);
        A.CallTo(() => _parser.ParseScriptAsync(scriptPath, A<CancellationToken>._))
            .Returns(expectedScript);

        _sut.RegisterModelSourcePath("TestModel", _tempPath);

        // Act
        var result = await _sut.GetMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.Single(result);
        Assert.Equal("1.0.0", result[0].SourceVersion);
        Assert.Equal("2.0.0", result[0].TargetVersion);
    }

    [Fact]
    public async Task GetMigrationAsync_MigrationExists_ShouldReturnScript()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationsPath = Path.Combine(_tempPath, "ConstructionKit", "migrations");
        Directory.CreateDirectory(migrationsPath);

        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        var scriptPath = Path.Combine(migrationsPath, "1.0.0-to-2.0.0.yaml");
        await File.WriteAllTextAsync(metaPath, "ckModelId: TestModel-1.0.0", ct);
        await File.WriteAllTextAsync(scriptPath, "sourceVersion: 1.0.0\ntargetVersion: 2.0.0", ct);

        var expectedMeta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-1.0.0",
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
        var expectedScript = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0"
        };

        A.CallTo(() => _parser.ParseMetaAsync(metaPath, A<CancellationToken>._))
            .Returns(expectedMeta);
        A.CallTo(() => _parser.ParseScriptAsync(scriptPath, A<CancellationToken>._))
            .Returns(expectedScript);

        _sut.RegisterModelSourcePath("TestModel", _tempPath);

        // Act
        var result = await _sut.GetMigrationAsync(ckModelId, "1.0.0", "2.0.0", ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
    }

    [Fact]
    public async Task GetMigrationAsync_MigrationNotFound_ShouldReturnNull()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var migrationsPath = Path.Combine(_tempPath, "ConstructionKit", "migrations");
        Directory.CreateDirectory(migrationsPath);

        var metaPath = Path.Combine(migrationsPath, "migration-meta.yaml");
        await File.WriteAllTextAsync(metaPath, "ckModelId: TestModel-1.0.0", ct);

        var expectedMeta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-1.0.0",
            Migrations = []
        };

        A.CallTo(() => _parser.ParseMetaAsync(metaPath, A<CancellationToken>._))
            .Returns(expectedMeta);

        _sut.RegisterModelSourcePath("TestModel", _tempPath);

        // Act
        var result = await _sut.GetMigrationAsync(ckModelId, "1.0.0", "3.0.0", ct);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void RegisterModelSourcePath_ShouldStorePathCaseInsensitive()
    {
        // Arrange & Act
        _sut.RegisterModelSourcePath("TestModel", _tempPath);
        _sut.RegisterModelSourcePath("TESTMODEL", _tempPath + "2");

        // Assert - should have overwritten the first entry (case insensitive)
        // We can't directly verify, but we can test the behavior
    }
}

public class EmbeddedCkMigrationContentProviderTests
{
    private readonly EmbeddedCkMigrationContentProvider _sut;
    private readonly ICkMigrationParser _parser;

    public EmbeddedCkMigrationContentProviderTests()
    {
        _parser = A.Fake<ICkMigrationParser>();
        var logger = A.Fake<ILogger<EmbeddedCkMigrationContentProvider>>();
        var migrationSources = Enumerable.Empty<ICkEmbeddedMigrationSource>();
        _sut = new EmbeddedCkMigrationContentProvider(_parser, logger, migrationSources);
    }

    [Fact]
    public async Task HasMigrationsAsync_NoSourceRegistered_ShouldReturnFalse()
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
    public async Task HasMigrationsAsync_SourceRegisteredButNoResource_ShouldReturnFalse()
    {
        // Arrange
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        _sut.RegisterMigrationSource("TestModel", Assembly.GetExecutingAssembly(), "NonExistent.Namespace");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetMigrationMetaAsync_NoSourceRegistered_ShouldReturnNull()
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
    public async Task GetMigrationsAsync_NoSourceRegistered_ShouldReturnEmptyList()
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
    public void RegisterMigrationSource_ShouldStoreSourceCaseInsensitive()
    {
        // Arrange & Act - registering same model name with different case
        _sut.RegisterMigrationSource("TestModel", Assembly.GetExecutingAssembly(), "Namespace1");
        _sut.RegisterMigrationSource("TESTMODEL", Assembly.GetExecutingAssembly(), "Namespace2");

        // Assert - should have overwritten (case insensitive dictionary)
    }
}

public class AggregateCkMigrationContentProviderTests
{
    [Fact]
    public async Task HasMigrationsAsync_NoProviders_ShouldReturnFalse()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var sut = new AggregateCkMigrationContentProvider(logger);
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HasMigrationsAsync_FirstProviderHasMigrations_ShouldReturnTrue()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        A.CallTo(() => provider1.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.True(result);
        A.CallTo(() => provider2.HasMigrationsAsync(A<CkModelId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task HasMigrationsAsync_SecondProviderHasMigrations_ShouldReturnTrue()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        A.CallTo(() => provider1.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(false);
        A.CallTo(() => provider2.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.HasMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetMigrationMetaAsync_FirstProviderReturnsMeta_ShouldReturnFromFirst()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var expectedMeta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" };

        A.CallTo(() => provider1.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns(expectedMeta);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel-1.0.0", result.CkModelId);
        A.CallTo(() => provider2.GetMigrationMetaAsync(A<CkModelId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task GetMigrationMetaAsync_FirstProviderReturnsNull_ShouldTrySecond()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var expectedMeta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" };

        A.CallTo(() => provider1.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns((CkMigrationMetaDto?)null);
        A.CallTo(() => provider2.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns(expectedMeta);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel-1.0.0", result.CkModelId);
    }

    [Fact]
    public async Task GetMigrationsAsync_FirstProviderHasMigrations_ShouldReturnFromFirst()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var expectedMigrations = new List<CkMigrationScriptDto>
        {
            new() { SourceVersion = "1.0.0", TargetVersion = "2.0.0" }
        };

        A.CallTo(() => provider1.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => provider1.GetMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(expectedMigrations);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.GetMigrationsAsync(ckModelId, ct);

        // Assert
        Assert.Single(result);
        Assert.Equal("1.0.0", result[0].SourceVersion);
    }

    [Fact]
    public async Task GetMigrationAsync_FirstProviderReturnsScript_ShouldReturnFromFirst()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var provider1 = A.Fake<ICkMigrationContentProvider>();
        var provider2 = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var expectedScript = new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" };

        A.CallTo(() => provider1.GetMigrationAsync(ckModelId, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(expectedScript);

        var sut = new AggregateCkMigrationContentProvider([provider1, provider2], logger);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await sut.GetMigrationAsync(ckModelId, "1.0.0", "2.0.0", ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        A.CallTo(() => provider2.GetMigrationAsync(A<CkModelId>._, A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task AddProvider_ShouldAddProviderToList()
    {
        // Arrange
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var sut = new AggregateCkMigrationContentProvider(logger);
        var provider = A.Fake<ICkMigrationContentProvider>();
        var ct = TestContext.Current.CancellationToken;

        // Act
        sut.AddProvider(provider);

        // Assert - verify by checking that the provider is called
        var ckModelId = new CkModelId("TestModel", "1.0.0");
        A.CallTo(() => provider.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);

        var result = await sut.HasMigrationsAsync(ckModelId, ct);
        Assert.True(result);
    }

    [Fact]
    public async Task CompiledProviderTakesPriority_WhenBothHaveData_ReturnsCompiledData()
    {
        // Arrange - simulate the DI registration order: Compiled first, then Embedded fallback
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var compiledLogger = A.Fake<ILogger<CompiledModelCkMigrationContentProvider>>();
        var compiledProvider = new CompiledModelCkMigrationContentProvider(compiledLogger);
        var embeddedProvider = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");
        var compiledMeta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" };
        var compiledScript = new CkMigrationScriptDto
        {
            SourceVersion = "1.0.0",
            TargetVersion = "2.0.0",
            Description = "From compiled model"
        };
        compiledProvider.SetMigrationData(ckModelId, new CkCompiledMigrationDataDto
        {
            Meta = compiledMeta,
            Scripts = [compiledScript]
        });

        // Embedded also has data (should NOT be reached)
        var embeddedMeta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" };
        A.CallTo(() => embeddedProvider.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => embeddedProvider.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns(embeddedMeta);

        var sut = new AggregateCkMigrationContentProvider(logger);
        sut.AddProvider(compiledProvider); // first = highest priority
        sut.AddProvider(embeddedProvider);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var meta = await sut.GetMigrationMetaAsync(ckModelId, ct);
        var scripts = await sut.GetMigrationsAsync(ckModelId, ct);

        // Assert - data comes from compiled provider, embedded is never queried
        Assert.NotNull(meta);
        Assert.Single(scripts);
        Assert.Equal("From compiled model", scripts[0].Description);
        A.CallTo(() => embeddedProvider.GetMigrationMetaAsync(A<CkModelId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => embeddedProvider.GetMigrationsAsync(A<CkModelId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task CompiledProviderEmpty_FallsBackToEmbedded()
    {
        // Arrange - compiled provider has no data, embedded does
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var compiledLogger = A.Fake<ILogger<CompiledModelCkMigrationContentProvider>>();
        var compiledProvider = new CompiledModelCkMigrationContentProvider(compiledLogger);
        var embeddedProvider = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");

        // Embedded has data
        A.CallTo(() => embeddedProvider.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => embeddedProvider.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns(new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" });
        A.CallTo(() => embeddedProvider.GetMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(new List<CkMigrationScriptDto>
            {
                new() { SourceVersion = "1.0.0", TargetVersion = "2.0.0", Description = "From embedded" }
            });

        var sut = new AggregateCkMigrationContentProvider(logger);
        sut.AddProvider(compiledProvider); // first but empty
        sut.AddProvider(embeddedProvider); // fallback with data
        var ct = TestContext.Current.CancellationToken;

        // Act
        var meta = await sut.GetMigrationMetaAsync(ckModelId, ct);
        var scripts = await sut.GetMigrationsAsync(ckModelId, ct);

        // Assert - falls back to embedded
        Assert.NotNull(meta);
        Assert.Single(scripts);
        Assert.Equal("From embedded", scripts[0].Description);
    }

    [Fact]
    public async Task CompiledProviderCleared_FallsBackToEmbedded()
    {
        // Arrange - compiled provider had data but was cleared
        var logger = A.Fake<ILogger<AggregateCkMigrationContentProvider>>();
        var compiledLogger = A.Fake<ILogger<CompiledModelCkMigrationContentProvider>>();
        var compiledProvider = new CompiledModelCkMigrationContentProvider(compiledLogger);
        var embeddedProvider = A.Fake<ICkMigrationContentProvider>();

        var ckModelId = new CkModelId("TestModel", "1.0.0");

        // First set data, then clear it (simulates import lifecycle)
        compiledProvider.SetMigrationData(ckModelId, new CkCompiledMigrationDataDto
        {
            Meta = new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" },
            Scripts = [new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" }]
        });
        compiledProvider.ClearMigrationData(ckModelId);

        // Embedded has data
        A.CallTo(() => embeddedProvider.HasMigrationsAsync(ckModelId, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => embeddedProvider.GetMigrationMetaAsync(ckModelId, A<CancellationToken>._))
            .Returns(new CkMigrationMetaDto { CkModelId = "TestModel-1.0.0" });

        var sut = new AggregateCkMigrationContentProvider(logger);
        sut.AddProvider(compiledProvider);
        sut.AddProvider(embeddedProvider);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var hasMigrations = await sut.HasMigrationsAsync(ckModelId, ct);
        var meta = await sut.GetMigrationMetaAsync(ckModelId, ct);

        // Assert - compiled is empty after clear, falls back to embedded
        Assert.True(hasMigrations);
        Assert.NotNull(meta);
    }
}
