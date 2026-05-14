using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

public class BlueprintYamlSerializerTests
{
    private readonly BlueprintYamlSerializer _serializer = new();

    #region Deserialization Tests

    [Fact]
    public void Deserialize_SimpleBlueprintYaml_ParsesCorrectly()
    {
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            description: A test blueprint
            """;

        var operationResult = new OperationResult();
        var result = _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult);

        Assert.NotNull(result);
        Assert.Equal("TestBlueprint-1.0.0", result.BlueprintId.FullName);
        Assert.Equal("A test blueprint", result.Description);
    }

    [Fact]
    public void Deserialize_WithCkModelDependencies_ParsesCorrectly()
    {
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            description: A test blueprint
            ckModelDependencies:
              - System-[2.0,)
              - Commerce-[1.0,2.0)
            """;

        var operationResult = new OperationResult();
        var result = _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult);

        Assert.NotNull(result);
        Assert.NotNull(result.CkModelDependencies);
        Assert.Equal(2, result.CkModelDependencies.Count);
        Assert.Equal("System", result.CkModelDependencies[0].Name);
        Assert.Equal("[2.0,)", result.CkModelDependencies[0].ModelVersionRange.ToString());
        Assert.Equal("Commerce", result.CkModelDependencies[1].Name);
    }

    [Fact]
    public void Deserialize_WithSeedDataPath_ParsesCorrectly()
    {
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            description: A test blueprint
            seedDataPath: seed-data/initial-entities.yaml
            """;

        var operationResult = new OperationResult();
        var result = _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult);

        Assert.NotNull(result);
        Assert.Equal("seed-data/initial-entities.yaml", result.SeedDataPath);
    }

    [Fact]
    public void Deserialize_CompleteBlueprint_ParsesCorrectly()
    {
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: InfrastructureStarter-1.0.0
            description: Infrastructure management starter blueprint
            ckModelDependencies:
              - System-[2.0,)
            seedDataPath: seed-data/initial-entities.yaml
            """;

        var operationResult = new OperationResult();
        var result = _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult);

        Assert.NotNull(result);
        Assert.Equal("InfrastructureStarter-1.0.0", result.BlueprintId.FullName);
        Assert.Equal("Infrastructure management starter blueprint", result.Description);
        Assert.NotNull(result.CkModelDependencies);
        Assert.Single(result.CkModelDependencies);
        Assert.Equal("seed-data/initial-entities.yaml", result.SeedDataPath);
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public async Task Serialize_SimpleBlueprint_ProducesValidYaml()
    {
        var blueprint = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("TestBlueprint", "1.0.0"),
            Description = "A test blueprint"
        };

        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _serializer.SerializeAsync(writer, blueprint);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var yaml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("blueprintId: TestBlueprint-1.0.0", yaml);
        Assert.Contains("description: A test blueprint", yaml);
    }

    [Fact]
    public async Task Serialize_WithCkModelDependencies_ProducesValidYaml()
    {
        var blueprint = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("TestBlueprint", "1.0.0"),
            Description = "A test blueprint",
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            }
        };

        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _serializer.SerializeAsync(writer, blueprint);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var yaml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("ckModelDependencies:", yaml);
        Assert.Contains("System-[2.0,)", yaml);
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public async Task Roundtrip_SerializeAndDeserialize_MaintainsData()
    {
        var original = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId("TestBlueprint", "1.0.0"),
            Description = "A test blueprint",
            CkModelDependencies = new List<CkModelIdVersionRange>
            {
                new("System", "[2.0,)")
            },
            SeedDataPath = "seed-data/test.yaml"
        };

        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _serializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var yaml = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        var operationResult = new OperationResult();
        var deserialized = _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult);

        Assert.Equal(original.BlueprintId.FullName, deserialized.BlueprintId.FullName);
        Assert.Equal(original.Description, deserialized.Description);
        Assert.Equal(original.SeedDataPath, deserialized.SeedDataPath);
        Assert.NotNull(deserialized.CkModelDependencies);
        Assert.Single(deserialized.CkModelDependencies);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Deserialize_EmptyYaml_ThrowsException()
    {
        var yaml = "";
        var operationResult = new OperationResult();

        Assert.Throws<BlueprintCatalogException>(() =>
            _serializer.DeserializeBlueprintMeta(yaml, "test.yaml", operationResult));
    }

    #endregion
}
