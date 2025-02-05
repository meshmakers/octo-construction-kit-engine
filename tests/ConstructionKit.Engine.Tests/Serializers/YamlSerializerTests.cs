using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.elements;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

public class YamlSerializerTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public YamlSerializerTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task DeserializeElementsAsync_types_ok()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/types-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckYamlSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_attributes_ok()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/attributes-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckYamlSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_associations_ok()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/associations-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckYamlSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_noSchema_ok()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/noSchema.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckYamlSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckYamlSerializer);
        Assert.Equal(4, ckElementsDto.Types?.Count);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_noSchema_malFormed_fail()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/noSchema_malformed.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckYamlSerializer.DeserializeElementsAsync(stream,
            filePath, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttribute_Fail()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/malformedAttribute.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckYamlSerializer.DeserializeElementsAsync(stream,
            filePath, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttributeValue_Fail()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var filePath = "sampleData/files/malformedAttributeValue.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () => await ckYamlSerializer.DeserializeElementsAsync(stream, filePath,
            operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task SerializeAsync_ok()
    {
        var ckYamlSerializer = new CkYamlSerializer(new CkSchemaValidator());

        var stream = new MemoryStream();
        await using var streamWriter = new StreamWriter(stream);
        var ckElementsDto = Builder.Build();
        await ckYamlSerializer.SerializeAsync(streamWriter, ckElementsDto);
        await streamWriter.FlushAsync(TestContext.Current.CancellationToken);

        stream.Position = 0;

        var streamReader = new StreamReader(stream);
        var yaml = await streamReader.ReadToEndAsync(TestContext.Current.CancellationToken);
        _testOutputHelper.WriteLine("output:");
        _testOutputHelper.WriteLine(yaml);
        Assert.NotNull(yaml);
        Assert.Contains("$schema", yaml);
    }
}