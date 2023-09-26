using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Engine.Serialization;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.models;
using Xunit.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serializers;

public class JsonSerializerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public JsonSerializerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task DeserializeAsync_types_ok()
    {
        var rtJsonSerializer = new RtJsonSerializer();

        var filePath = "sampleData/files/entity-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var rtModelRootDto = await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult);
        Assert.NotNull(rtModelRootDto);
        Assert.Equal(2, rtModelRootDto.Dependencies.Count);
        Assert.Single(rtModelRootDto.Entities);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    //
    // [Fact]
    // public async Task DeserializeAsync_attributes_ok()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/attributes-ok.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     var ckElementsDto = await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult);
    //     Assert.NotNull(ckElementsDto);
    //     Assert.Empty(operationResult.Messages);
    //     Assert.False(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    // }
    //
    // [Fact]
    // public async Task DeserializeAsync_associations_ok()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/associations-ok.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     var ckElementsDto = await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult);
    //     Assert.NotNull(ckElementsDto);
    //     Assert.Empty(operationResult.Messages);
    //     Assert.False(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    // }
    //
    // [Fact]
    // public async Task DeserializeAsync_noSchema_ok()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/noSchema.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult);
    //     Assert.Empty(operationResult.Messages);
    //     Assert.False(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    // }
    //
    // [Fact]
    // public async Task DeserializeAsync_noSchema_malFormed_fail()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/noSchema_malformed.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     await Assert.ThrowsAsync<ModelParseException>(async () => await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult));
    //     Assert.Single(operationResult.Messages);
    //     Assert.True(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    //     Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    // }
    //
    // [Fact]
    // public async Task DeserializeAsync_MalformedAttribute_Fail()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/noSchema_malformed.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     await Assert.ThrowsAsync<ModelParseException>(async () => await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult));
    //     Assert.Single(operationResult.Messages);
    //     Assert.True(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    //     Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    // }
    //     
    // [Fact]
    // public async Task DeserializeAsync_MalformedAttributeValue_Fail()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var filePath = "sampleData/files/malformedAttributeValue.json";
    //     var stream = File.OpenRead(filePath);
    //     var operationResult = new OperationResult();
    //     await Assert.ThrowsAsync<ModelParseException>(async () => await rtJsonSerializer.DeserializeAsync(stream, filePath, operationResult));
    //     Assert.Single(operationResult.Messages);
    //     Assert.True(operationResult.HasErrors);
    //     Assert.False(operationResult.HasFatalErrors);
    //     Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    // }
    //
    // [Fact]
    // public async Task SerializeAsync_ok()
    // {
    //     var rtJsonSerializer = new RtJsonSerializer();
    //
    //     var stream = new MemoryStream();
    //     await using var streamWriter = new StreamWriter(stream);
    //     var ckElementsDto = Builder.Build();
    //     await rtJsonSerializer.SerializeAsync(streamWriter, ckElementsDto);
    //     await streamWriter.FlushAsync();
    //
    //     stream.Position = 0;
    //     var streamReader = new StreamReader(stream);
    //     var json = await streamReader.ReadToEndAsync();
    //     _testOutputHelper.WriteLine("output:");
    //     _testOutputHelper.WriteLine(json);
    //     Assert.NotNull(json);
    //     Assert.Contains("$schema", json);
    // }
}