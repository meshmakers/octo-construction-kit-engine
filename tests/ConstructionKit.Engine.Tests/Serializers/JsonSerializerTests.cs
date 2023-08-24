using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Xunit.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests.Serializers;

public class JsonSerializerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public JsonSerializerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_types_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/types-ok.json");
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_attributes_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/attributes-ok.json");
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_associations_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/associations-ok.json");
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_noSchema_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/noSchema.json");
        var operationResult = new OperationResult();
        await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_noSchema_malFormed_fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/noSchema_malformed.json");
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttribute_Fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/malformedAttribute.json");
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
        
    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttributeValue_Fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = File.OpenRead("sampleData/files/malformedAttributeValue.json");
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckJsonSerializer.DeserializeElementsAsync(stream, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public async Task SerializeAsync_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();
    
        var stream = new MemoryStream();
        await using var streamWriter = new StreamWriter(stream);
        var ckElementsDto = sampleData.elements.Builder.Build();
        await ckJsonSerializer.SerializeAsync(streamWriter, ckElementsDto);
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