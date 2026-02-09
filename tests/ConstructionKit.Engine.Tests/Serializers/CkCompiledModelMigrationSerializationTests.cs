using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

public class CkCompiledModelMigrationSerializationTests
{
    private readonly CkYamlSerializer _yamlSerializer = new(new CkSchemaValidator());
    private readonly CkJsonSerializer _jsonSerializer = new();

    private static CkCompiledModelRoot CreateCompiledModelWithMigrations()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-2.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")],
            Migrations = new CkCompiledMigrationDataDto
            {
                Meta = new CkMigrationMetaDto
                {
                    CkModelId = "Test-2.0.0",
                    Migrations =
                    [
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "1.0.0",
                            ToVersion = "2.0.0",
                            ScriptPath = "1-0-0-to-2-0-0.yaml",
                            Description = "Migrate from v1 to v2"
                        }
                    ]
                },
                Scripts =
                [
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "1.0.0",
                        TargetVersion = "2.0.0",
                        Description = "Rename attribute",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/MyType" },
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.RenameAttribute,
                                    SourceAttribute = "OldName",
                                    TargetAttribute = "NewName"
                                }
                            }
                        ]
                    }
                ]
            }
        };
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithMigrations_PreservesMigrationData()
    {
        var original = CreateCompiledModelWithMigrations();

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal("Test-2.0.0", deserialized.Migrations.Meta.CkModelId);
        Assert.Single(deserialized.Migrations.Meta.Migrations);
        Assert.Equal("1.0.0", deserialized.Migrations.Meta.Migrations[0].FromVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Meta.Migrations[0].ToVersion);
        Assert.Single(deserialized.Migrations.Scripts);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[0].TargetVersion);
        Assert.Single(deserialized.Migrations.Scripts[0].Steps);
        Assert.Equal("step-1", deserialized.Migrations.Scripts[0].Steps[0].StepId);
        Assert.Equal(CkMigrationTransformType.RenameAttribute,
            deserialized.Migrations.Scripts[0].Steps[0].Transform!.Type);
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithoutMigrations_MigrationsIsNull()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-1.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")]
        };

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Null(deserialized.Migrations);
    }

    [Fact]
    public async Task RoundTrip_Json_WithMigrations_PreservesMigrationData()
    {
        var original = CreateCompiledModelWithMigrations();

        // Serialize to JSON
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _jsonSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize from JSON
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _jsonSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal("Test-2.0.0", deserialized.Migrations.Meta.CkModelId);
        Assert.Single(deserialized.Migrations.Meta.Migrations);
        Assert.Equal("1.0.0", deserialized.Migrations.Meta.Migrations[0].FromVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Meta.Migrations[0].ToVersion);
        Assert.Single(deserialized.Migrations.Scripts);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[0].TargetVersion);
        Assert.Single(deserialized.Migrations.Scripts[0].Steps);
        Assert.Equal(CkMigrationTransformType.RenameAttribute,
            deserialized.Migrations.Scripts[0].Steps[0].Transform!.Type);
    }

    [Fact]
    public async Task RoundTrip_Json_WithoutMigrations_MigrationsIsNull()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-1.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")]
        };

        // Serialize to JSON
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _jsonSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize from JSON
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _jsonSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Null(deserialized.Migrations);
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithMultipleMigrations_PreservesAll()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-3.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")],
            Migrations = new CkCompiledMigrationDataDto
            {
                Meta = new CkMigrationMetaDto
                {
                    CkModelId = "Test-3.0.0",
                    Migrations =
                    [
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "1.0.0",
                            ToVersion = "3.0.0",
                            ScriptPath = "1-0-0-to-3-0-0.yaml"
                        },
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "2.0.0",
                            ToVersion = "3.0.0",
                            ScriptPath = "2-0-0-to-3-0-0.yaml"
                        }
                    ]
                },
                Scripts =
                [
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "1.0.0",
                        TargetVersion = "3.0.0",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.ChangeCkType,
                                    NewCkTypeId = "${Test}/NewType"
                                }
                            }
                        ]
                    },
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "2.0.0",
                        TargetVersion = "3.0.0",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.SetValue,
                                    TargetAttribute = "Status",
                                    Value = "Active"
                                }
                            }
                        ]
                    }
                ]
            }
        };

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal(2, deserialized.Migrations.Meta.Migrations.Count);
        Assert.Equal(2, deserialized.Migrations.Scripts.Count);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[1].SourceVersion);
    }
}
