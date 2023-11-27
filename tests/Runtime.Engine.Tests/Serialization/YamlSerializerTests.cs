using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Xunit.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

public class YamlSerializerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public YamlSerializerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task DeserializeAsync_types_ok()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());

        var filePath = "sampleData/files/entity-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);
        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.Equal(2, rtModelRootDto.Entities.Count);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    
    [Fact]
    public async Task DeserializeAsync_noSchema_ok()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());
    
        var filePath = "sampleData/files/noSchema.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task DeserializeAsync_noSchema_malFormed_fail()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());
    
        var filePath = "sampleData/files/noSchema_malformed.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<RuntimeModelParseException>(async () => await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult));
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
        Assert.Equal(1, operationResult.Messages[1].MessageNumber);
    }
        
    [Fact]
    public async Task DeserializeAsync_MalformedAttributeValue_Fail()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());
    
        var filePath = "sampleData/files/malformedAttributeValue.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<RuntimeModelParseException>(async () => await rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public async Task SerializeAsync_ok()
    {
        var rtYamlSerializer = new RtYamlSerializer(new RtSchemaValidator());
    
        var stream = new MemoryStream();
        await using var streamWriter = new StreamWriter(stream);
        var ckElementsDto = sampleData.models.Builder.Build();
        await rtYamlSerializer.SerializeAsync(streamWriter, ckElementsDto);
        await streamWriter.FlushAsync();
    
        stream.Position = 0;
        var streamReader = new StreamReader(stream);
        var json = await streamReader.ReadToEndAsync();
        _testOutputHelper.WriteLine("output:");
        _testOutputHelper.WriteLine(json);
        Assert.NotNull(json);
        Assert.Contains("$schema", json);
    }
}