using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

public class VersionRangeDeserializationTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public VersionRangeDeserializationTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    #region YAML Deserialization

    [Fact]
    public async Task DeserializeYaml_VersionRangeDependencies_ParsedCorrectly()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var filePath = "sampleData/files/entity-version-ranges.yaml";
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);

        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);

        Assert.Equal("System", rtModelRootDto.Dependencies[0].Name);
        Assert.Equal("[1.0,2.0)", rtModelRootDto.Dependencies[0].ModelVersionRange.ToString());

        Assert.Equal("Sample1", rtModelRootDto.Dependencies[1].Name);
        Assert.Equal("[1.0,2.0)", rtModelRootDto.Dependencies[1].ModelVersionRange.ToString());
    }

    [Fact]
    public async Task DeserializeYaml_ExactVersionDependencies_ParsedAsMinimumRange()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var filePath = "sampleData/files/entity-exact-version-deps.yaml";
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);

        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.False(operationResult.HasErrors);

        // "System-1.0.0" is parsed as >= 1.0.0 (minimum inclusive)
        Assert.Equal("System", rtModelRootDto.Dependencies[0].Name);
        Assert.True(rtModelRootDto.Dependencies[0].IsSatisfiedBy(new CkModelId("System-1.0.0")));
        Assert.True(rtModelRootDto.Dependencies[0].IsSatisfiedBy(new CkModelId("System-1.0.1")));
        Assert.True(rtModelRootDto.Dependencies[0].IsSatisfiedBy(new CkModelId("System-2.0.0")));

        // "Sample1-2.0.0" is parsed as >= 2.0.0 (minimum inclusive)
        Assert.Equal("Sample1", rtModelRootDto.Dependencies[1].Name);
        Assert.True(rtModelRootDto.Dependencies[1].IsSatisfiedBy(new CkModelId("Sample1-2.0.0")));
        Assert.True(rtModelRootDto.Dependencies[1].IsSatisfiedBy(new CkModelId("Sample1-2.0.1")));
        Assert.False(rtModelRootDto.Dependencies[1].IsSatisfiedBy(new CkModelId("Sample1-1.9.0")));
    }

    [Fact]
    public async Task DeserializeYaml_SimpleNameDependencies_BackwardCompatible()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var filePath = "sampleData/files/entity-ok.yaml";
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);

        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.False(operationResult.HasErrors);

        // "System" without version defaults to >= 1.0.0
        Assert.Equal("System", rtModelRootDto.Dependencies[0].Name);
        Assert.True(rtModelRootDto.Dependencies[0].IsSatisfiedBy(new CkModelId("System-1.0.0")));
        Assert.True(rtModelRootDto.Dependencies[0].IsSatisfiedBy(new CkModelId("System-2.0.0")));
    }

    #endregion

    #region JSON Deserialization

    [Fact]
    public async Task DeserializeJson_VersionRangeDependencies_ParsedCorrectly()
    {
        var rtJsonSerializer = new RtJsonSerializer();

        var filePath = "sampleData/files/entity-version-ranges.json";
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult);

        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);

        Assert.Equal("System", rtModelRootDto.Dependencies[0].Name);
        Assert.Equal("[1.0,2.0)", rtModelRootDto.Dependencies[0].ModelVersionRange.ToString());

        Assert.Equal("Sample1", rtModelRootDto.Dependencies[1].Name);
        Assert.Equal("[1.0,2.0)", rtModelRootDto.Dependencies[1].ModelVersionRange.ToString());
    }

    [Fact]
    public async Task DeserializeJsonStream_VersionRangeDependencies_ParsedCorrectly()
    {
        var rtJsonSerializer = new RtJsonSerializer();

        var filePath = "sampleData/files/entity-version-ranges.json";
        await using var stream = File.OpenRead(filePath);
        var entities = new List<RtEntityTcDto>();

        var rtDeserializeStream = await rtJsonSerializer.DeserializeStreamAsync(stream);
        rtDeserializeStream.BulkDeserialized += (_, args) =>
        {
            entities.AddRange(args.DeserializedEntities);
            args.IsHandled = true;
        };
        await rtDeserializeStream.ReadAsync();

        Assert.Equal(2, rtDeserializeStream.Dependencies.Count);
        Assert.Equal(2, entities.Count);

        var deps = rtDeserializeStream.Dependencies.ToList();
        Assert.Equal("System", deps[0].Name);
        Assert.Equal("[1.0,2.0)", deps[0].ModelVersionRange.ToString());
    }

    #endregion

    #region Round-Trip Serialization

    [Fact]
    public async Task SerializeYaml_VersionRangeDependencies_RoundTrip()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var modelRoot = new RtModelRootTcDto
        {
            Dependencies =
            {
                new CkModelIdVersionRange("Basic-[2.0,3.0)"),
                new CkModelIdVersionRange("Industry.Energy-[2.0,3.0)")
            },
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = OctoObjectId.GenerateNewId(),
                    CkTypeId = "Basic/Tree",
                    Attributes =
                    {
                        new RtAttributeTcDto { Id = "System/Name", Value = "Test" }
                    }
                }
            }
        };

        // Serialize
        var stream = new MemoryStream();
        await using var streamWriter = new StreamWriter(stream);
        await rtYamlSerializer.SerializeAsync(streamWriter, modelRoot);
        await streamWriter.FlushAsync(TestContext.Current.CancellationToken);

        // Log output
        stream.Position = 0;
        var yamlContent = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);
        _testOutputHelper.WriteLine("Serialized YAML:");
        _testOutputHelper.WriteLine(yamlContent);

        // Verify version ranges are present in output
        Assert.Contains("Basic-[2.0,3.0)", yamlContent);
        Assert.Contains("Industry.Energy-[2.0,3.0)", yamlContent);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await rtYamlSerializer.DeserializeAsync(stream, "test", operationResult);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Dependencies.Count);
        Assert.False(operationResult.HasErrors);

        Assert.Equal("Basic", deserialized.Dependencies[0].Name);
        Assert.Equal("[2.0,3.0)", deserialized.Dependencies[0].ModelVersionRange.ToString());
        Assert.Equal("Industry.Energy", deserialized.Dependencies[1].Name);
        Assert.Equal("[2.0,3.0)", deserialized.Dependencies[1].ModelVersionRange.ToString());
    }

    [Fact]
    public async Task DeserializeYaml_VersionRangeDependencies_SatisfiedByCorrectVersions()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var filePath = "sampleData/files/entity-version-ranges.yaml";
        await using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);

        var systemDep = rtModelRootDto.Dependencies[0]; // System-[1.0,2.0)

        // Versions within range
        Assert.True(systemDep.IsSatisfiedBy(new CkModelId("System-1.0.0")));
        Assert.True(systemDep.IsSatisfiedBy(new CkModelId("System-1.5.0")));
        Assert.True(systemDep.IsSatisfiedBy(new CkModelId("System-1.9.9")));

        // Versions outside range
        Assert.False(systemDep.IsSatisfiedBy(new CkModelId("System-0.9.0")));
        Assert.False(systemDep.IsSatisfiedBy(new CkModelId("System-2.0.0"))); // exclusive upper bound
        Assert.False(systemDep.IsSatisfiedBy(new CkModelId("System-3.0.0")));

        // Wrong model name
        Assert.False(systemDep.IsSatisfiedBy(new CkModelId("Sample1-1.5.0")));
    }

    #endregion
}
