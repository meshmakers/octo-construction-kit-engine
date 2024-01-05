using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Engine.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

public class RtSchemaValidatorTests
{
    [Fact]
    public void ValidateModelInJson_ok()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/entity-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInJson(stream, filePath, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateModelInJson_MalformedAttribute_Fail()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/malformedAttribute.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
        Assert.Equal(1, operationResult.Messages[1].MessageNumber);
    }

    [Fact]
    public void ValidateModelInJson_MalformedAttributeValue_Fail()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/malformedAttributeValue.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
    }


    [Fact]
    public void ValidateModelInYaml_ok()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/entity-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInYaml(stream, filePath, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateModelInYaml_MalformedAttribute_Fail()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/malformedAttribute.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
        Assert.Equal(1, operationResult.Messages[1].MessageNumber);
    }

    [Fact]
    public void ValidateModelInYaml_MalformedAttributeValue_Fail()
    {
        var schemaValidator = new RtSchemaValidator();

        var filePath = "sampleData/files/malformedAttributeValue.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(1, operationResult.Messages[0].MessageNumber);
    }
}