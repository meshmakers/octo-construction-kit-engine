using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

public class CkSchemaValidatorTests
{
    [Fact]
    public void ValidateElementsInJson_ok()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/types-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInJson(stream, filePath, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateElementsInJson_MalformedAttribute_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/malformedAttribute.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateElementsInJson_MalformedAttributeValue_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/malformedAttributeValue.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateMetaInJson_WithValidElementsFile_ShouldFail()
    {
        var schemaValidator = new CkSchemaValidator();

        // Using elements file for meta validation should fail as it doesn't match meta schema
        var filePath = "sampleData/files/types-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateMetaInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateCompiledModelInJson_WithValidElementsFile_ShouldFail()
    {
        var schemaValidator = new CkSchemaValidator();

        // Using elements file for compiled model validation should fail as it doesn't match compiled model schema
        var filePath = "sampleData/files/types-ok.json";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateCompiledModelInJson(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateElementsInYaml_ok()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/types-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateElementsInYaml_MalformedAttribute_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/malformedAttribute.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateElementsInYaml_MalformedAttributeValue_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/malformedAttributeValue.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateModelConfigInYaml_WithValidElementsFile_ShouldFail()
    {
        var schemaValidator = new CkSchemaValidator();

        // Using elements file for model config validation should fail as it doesn't match model config schema
        var filePath = "sampleData/files/types-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateModelConfigInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateMetaInYaml_WithValidElementsFile_ShouldFail()
    {
        var schemaValidator = new CkSchemaValidator();

        // Using elements file for meta validation should fail as it doesn't match meta schema
        var filePath = "sampleData/files/types-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateMetaInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void ValidateCompiledModelInYaml_WithValidElementsFile_ShouldFail()
    {
        var schemaValidator = new CkSchemaValidator();

        // Using elements file for compiled model validation should fail as it doesn't match compiled model schema
        var filePath = "sampleData/files/types-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateCompiledModelInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
}