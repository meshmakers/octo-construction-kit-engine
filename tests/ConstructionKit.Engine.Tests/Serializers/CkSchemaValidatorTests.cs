using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests.Serializers;

public class CkSchemaValidatorTests
{
    
    [Fact]
    public void ValidateElementsInJson_ok()
    {
        var schemaValidator = new CkSchemaValidator();

        var stream = File.OpenRead("sampleData/files/types-ok.json");
        var operationResult = new OperationResult();
        bool isValid = schemaValidator.ValidateElementsInJson(stream, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public void ValidateElementsInJson_MalformedAttribute_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var stream = File.OpenRead("sampleData/files/malformedAttribute.json");
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInJson(stream, operationResult);
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

        var stream = File.OpenRead("sampleData/files/malformedAttributeValue.json");
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInJson(stream, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
    
    
    [Fact]
    public void ValidateElementsInYaml_ok()
    {
        var schemaValidator = new CkSchemaValidator();

        var stream = File.OpenRead("sampleData/files/types-ok.yaml");
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, operationResult);
        Assert.True(isValid);
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public void ValidateElementsInYaml_MalformedAttribute_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var stream = File.OpenRead("sampleData/files/malformedAttribute.yaml");
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, operationResult);
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

        var stream = File.OpenRead("sampleData/files/malformedAttributeValue.yaml");
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, operationResult);
        Assert.False(isValid);
        Assert.Single(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
    }
}