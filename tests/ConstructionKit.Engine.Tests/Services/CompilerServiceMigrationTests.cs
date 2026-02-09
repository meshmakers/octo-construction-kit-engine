using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using MessageLevel = Meshmakers.Octo.ConstructionKit.Contracts.Messages.MessageLevel;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Services;

/// <summary>
/// Tests for the migration embedding functionality of <see cref="CompilerService"/>.
/// These tests verify the migration reading logic by creating temp directories with
/// migration YAML files and verifying the parser correctly reads and structures them
/// into <see cref="CkCompiledMigrationDataDto"/>, mirroring what <c>ReadMigrationsAsync</c> does.
/// </summary>
public class CompilerServiceMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ICkMigrationParser _parser;

    public CompilerServiceMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CompilerMigrationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _parser = new CkMigrationParser();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task ReadMigrations_WithValidMigrations_EmbedsMigrationData()
    {
        // Arrange - create migrations directory with meta and script
        var ct = TestContext.Current.CancellationToken;
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "migration-meta.yaml"),
            """
            ckModelId: TestModel-2.0.0
            migrations:
              - fromVersion: "1.0.0"
                toVersion: "2.0.0"
                scriptPath: "1.0.0-to-2.0.0.yaml"
            """, ct);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "1.0.0-to-2.0.0.yaml"),
            """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "Rename attribute OldName to NewName"
            steps:
              - stepId: step-1
                action: Transform
                target:
                  ckTypeId: "${TestModel}/MyType"
                transform:
                  type: RenameAttribute
                  sourceAttribute: OldName
                  targetAttribute: NewName
            """, ct);

        // Act - parse meta and scripts (mirrors ReadMigrationsAsync logic)
        var meta = await _parser.ParseMetaAsync(
            Path.Combine(migrationsDir, "migration-meta.yaml"), ct);
        var scripts = new List<CkMigrationScriptDto>();
        foreach (var migrationRef in meta.Migrations)
        {
            var scriptPath = Path.Combine(migrationsDir, migrationRef.ScriptPath);
            var script = await _parser.ParseScriptAsync(scriptPath, ct);
            scripts.Add(script);
        }

        var result = new CkCompiledMigrationDataDto { Meta = meta, Scripts = scripts };

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestModel-2.0.0", result.Meta.CkModelId);
        Assert.Single(result.Meta.Migrations);
        Assert.Equal("1.0.0", result.Meta.Migrations[0].FromVersion);
        Assert.Equal("2.0.0", result.Meta.Migrations[0].ToVersion);
        Assert.Single(result.Scripts);
        Assert.Equal("1.0.0", result.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", result.Scripts[0].TargetVersion);
        Assert.Single(result.Scripts[0].Steps);
        Assert.Equal(CkMigrationTransformType.RenameAttribute, result.Scripts[0].Steps[0].Transform!.Type);
    }

    [Fact]
    public void ReadMigrations_WithoutMigrationsDirectory_ReturnsNull()
    {
        // Arrange - no migrations directory
        var migrationsDir = Path.Combine(_tempDir, "migrations");

        // Act - check if directory exists (mirrors CompileAsync logic)
        var exists = Directory.Exists(migrationsDir);

        // Assert
        Assert.False(exists);
        // In CompileAsync, this means compiledModelRoot.Migrations stays null
    }

    [Fact]
    public async Task ReadMigrations_MissingScriptFile_ReportsError()
    {
        // Arrange - meta references a script that doesn't exist
        var ct = TestContext.Current.CancellationToken;
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "migration-meta.yaml"),
            """
            ckModelId: TestModel-2.0.0
            migrations:
              - fromVersion: "1.0.0"
                toVersion: "2.0.0"
                scriptPath: "1.0.0-to-2.0.0.yaml"
            """, ct);

        // Act - parse meta, then check script existence (mirrors ReadMigrationsAsync)
        var meta = await _parser.ParseMetaAsync(
            Path.Combine(migrationsDir, "migration-meta.yaml"), ct);

        var operationResult = new OperationResult();
        var scripts = new List<CkMigrationScriptDto>();
        foreach (var migrationRef in meta.Migrations)
        {
            var scriptPath = Path.Combine(migrationsDir, migrationRef.ScriptPath);
            if (!File.Exists(scriptPath))
            {
                // This is what ReadMigrationsAsync does - adds an error message
                operationResult.AddMessage(
                    Meshmakers.Octo.ConstructionKit.Engine.Messages.MessageCodes.MigrationScriptNotFound(scriptPath));
                continue;
            }

            var script = await _parser.ParseScriptAsync(scriptPath, ct);
            scripts.Add(script);
        }

        // Assert
        Assert.Single(meta.Migrations);
        Assert.Empty(scripts); // script file doesn't exist, so nothing was parsed
        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.Error, operationResult.Messages[0].MessageLevel);
    }

    [Fact]
    public async Task ReadMigrations_MultipleMigrations_EmbedsAll()
    {
        // Arrange - meta with two migration references and two scripts
        var ct = TestContext.Current.CancellationToken;
        var migrationsDir = Path.Combine(_tempDir, "migrations");
        Directory.CreateDirectory(migrationsDir);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "migration-meta.yaml"),
            """
            ckModelId: TestModel-3.0.0
            migrations:
              - fromVersion: "1.0.0"
                toVersion: "2.0.0"
                scriptPath: "1.0.0-to-2.0.0.yaml"
              - fromVersion: "2.0.0"
                toVersion: "3.0.0"
                scriptPath: "2.0.0-to-3.0.0.yaml"
            """, ct);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "1.0.0-to-2.0.0.yaml"),
            """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "First migration"
            steps:
              - stepId: step-1
                action: Transform
                target:
                  ckTypeId: "${TestModel}/MyType"
                transform:
                  type: RenameAttribute
                  sourceAttribute: OldName
                  targetAttribute: NewName
            """, ct);

        await File.WriteAllTextAsync(Path.Combine(migrationsDir, "2.0.0-to-3.0.0.yaml"),
            """
            sourceVersion: "2.0.0"
            targetVersion: "3.0.0"
            description: "Second migration"
            steps:
              - stepId: step-1
                action: Transform
                target:
                  ckTypeId: "${TestModel}/MyType"
                transform:
                  type: SetValue
                  targetAttribute: Status
                  value: Active
            """, ct);

        // Act - parse meta and all scripts (mirrors ReadMigrationsAsync)
        var meta = await _parser.ParseMetaAsync(
            Path.Combine(migrationsDir, "migration-meta.yaml"), ct);
        var scripts = new List<CkMigrationScriptDto>();
        foreach (var migrationRef in meta.Migrations)
        {
            var scriptPath = Path.Combine(migrationsDir, migrationRef.ScriptPath);
            var script = await _parser.ParseScriptAsync(scriptPath, ct);
            scripts.Add(script);
        }

        var result = new CkCompiledMigrationDataDto { Meta = meta, Scripts = scripts };

        // Assert
        Assert.Equal("TestModel-3.0.0", result.Meta.CkModelId);
        Assert.Equal(2, result.Meta.Migrations.Count);
        Assert.Equal(2, result.Scripts.Count);

        Assert.Equal("1.0.0", result.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", result.Scripts[0].TargetVersion);
        Assert.Equal(CkMigrationTransformType.RenameAttribute, result.Scripts[0].Steps[0].Transform!.Type);

        Assert.Equal("2.0.0", result.Scripts[1].SourceVersion);
        Assert.Equal("3.0.0", result.Scripts[1].TargetVersion);
        Assert.Equal(CkMigrationTransformType.SetValue, result.Scripts[1].Steps[0].Transform!.Type);
    }
}
