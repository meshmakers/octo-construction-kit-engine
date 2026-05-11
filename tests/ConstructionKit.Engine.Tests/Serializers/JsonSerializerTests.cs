using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.elements;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

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

        var filePath = "sampleData/files/types-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_attributes_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/attributes-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_associations_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/associations-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var ckElementsDto = await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.NotNull(ckElementsDto);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_noSchema_ok()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/noSchema.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task DeserializeElementsAsync_noSchema_malFormed_fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/noSchema_malformed.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () =>
            await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttribute_Fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/noSchema_malformed.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () =>
            await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult));
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task DeserializeElementsAsync_MalformedAttributeValue_Fail()
    {
        var ckJsonSerializer = new CkJsonSerializer();

        var filePath = "sampleData/files/malformedAttributeValue.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        await Assert.ThrowsAsync<ModelParseException>(async () =>
            await ckJsonSerializer.DeserializeElementsAsync(stream, filePath, operationResult));
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
        var ckElementsDto = Builder.Build();
        await ckJsonSerializer.SerializeAsync(streamWriter, ckElementsDto);
        await streamWriter.FlushAsync(TestContext.Current.CancellationToken);

        stream.Position = 0;
        var streamReader = new StreamReader(stream);
        var json = await streamReader.ReadToEndAsync(TestContext.Current.CancellationToken);
        _testOutputHelper.WriteLine("output:");
        _testOutputHelper.WriteLine(json);
        Assert.NotNull(json);
        Assert.Contains("$schema", json);
    }

    private const string CompiledModelWithUnknownProperties = """
        {
          "$schema": "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json",
          "modelId": "Test-1.0.0",
          "dependencies": [],
          "types": [
            {
              "typeId": "Sample-1",
              "isStreamType": false,
              "obsoleteFlag": true
            }
          ],
          "attributes": [
            {
              "id": "Sample-1",
              "valueType": "String",
              "isDataStream": true
            }
          ]
        }
        """;

    [Fact]
    public async Task DeserializeCompiledModelRootAsync_StrictMode_FailsOnUnknownProperties()
    {
        var ckJsonSerializer = new CkJsonSerializer();
        var operationResult = new OperationResult();

        await Assert.ThrowsAsync<ModelParseException>(async () =>
            await ckJsonSerializer.DeserializeCompiledModelRootAsync(
                CompiledModelWithUnknownProperties, "test", operationResult));

        Assert.True(operationResult.HasErrors);
    }

    [Fact]
    public async Task DeserializeCompiledModelRootAsync_TolerantMode_DropsUnknownPropertiesSilently()
    {
        var ckJsonSerializer = new CkJsonSerializer();
        var operationResult = new OperationResult();

        var model = await ckJsonSerializer.DeserializeCompiledModelRootAsync(
            CompiledModelWithUnknownProperties, "test", operationResult,
            tolerantToUnknownProperties: true);

        Assert.False(operationResult.HasErrors);
        Assert.Equal("Test-1.0.0", model.ModelId.ToString());
        Assert.NotNull(model.Types);
        Assert.Single(model.Types);
        Assert.Equal("Sample", model.Types[0].TypeId.Name);
        Assert.NotNull(model.Attributes);
        Assert.Single(model.Attributes);
        Assert.Equal("Sample", model.Attributes[0].AttributeId.Name);
    }

    [Fact]
    public async Task DeserializeCompiledModelRootAsync_TolerantMode_IgnoresAnyOfBranchNoise()
    {
        // CkAttribute.defaultValues uses anyOf [string|boolean|number]. A real catalog file
        // observed in the wild stores enum defaults as quoted strings (e.g. defaultValues: ["1"]).
        // The schema engine reports the failing boolean/number branches as noise even though the
        // string branch matched. Combined with removed-property noise (isStreamType), the tolerant
        // path must still succeed.
        const string payload = """
            {
              "$schema": "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json",
              "modelId": "Test-1.0.0",
              "dependencies": [],
              "types": [
                { "typeId": "Sample-1", "isStreamType": false }
              ],
              "attributes": [
                {
                  "id": "State-1",
                  "valueType": "Enum",
                  "valueCkEnumId": "Test-1.0.0/State-1",
                  "defaultValues": ["1"]
                }
              ]
            }
            """;

        var ckJsonSerializer = new CkJsonSerializer();
        var operationResult = new OperationResult();

        var model = await ckJsonSerializer.DeserializeCompiledModelRootAsync(
            payload, "test", operationResult, tolerantToUnknownProperties: true);

        Assert.False(operationResult.HasErrors);
        Assert.Equal("Test-1.0.0", model.ModelId.ToString());
        Assert.NotNull(model.Attributes);
        Assert.Single(model.Attributes);
    }

    [Fact]
    public async Task DeserializeCompiledModelRootAsync_TolerantMode_StillFailsOnGenuineErrors()
    {
        var ckJsonSerializer = new CkJsonSerializer();
        var operationResult = new OperationResult();

        // missing required 'modelId' is a genuine schema violation that must not be tolerated
        const string invalid = """
            {
              "$schema": "https://schemas.meshmakers.cloud/construction-kit-compiled.schema.json",
              "types": [
                { "typeId": "Sample-1", "isStreamType": true }
              ]
            }
            """;

        await Assert.ThrowsAsync<ModelParseException>(async () =>
            await ckJsonSerializer.DeserializeCompiledModelRootAsync(
                invalid, "test", operationResult, tolerantToUnknownProperties: true));

        Assert.True(operationResult.HasErrors);
    }
}