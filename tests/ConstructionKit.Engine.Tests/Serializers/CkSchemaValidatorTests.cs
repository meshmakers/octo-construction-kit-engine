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

    #region Pascal Case Validation Tests

    [Fact]
    public void ValidateElementsInYaml_PascalCase_NamespacedIdentifiers_Ok()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-namespaced-ok.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.True(isValid, $"Validation failed with messages: {string.Join(", ", operationResult.Messages.Select(m => m.MessageText))}");
        Assert.False(operationResult.Messages.Any());
        Assert.False(operationResult.HasErrors);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_TypeIdLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-typeId-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("typeId", operationResult.Messages[0].MessageText);
        Assert.Contains("location", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_RecordIdLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-recordId-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("recordId", operationResult.Messages[0].MessageText);
        Assert.Contains("address", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_AttributeIdLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-attributeId-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("designation", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_EnumIdLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-enumId-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("enumId", operationResult.Messages[0].MessageText);
        Assert.Contains("status", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_AssociationRoleIdLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-associationRoleId-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("parentChild", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_AttributeNameLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-attributeName-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("name", operationResult.Messages[0].MessageText);
    }

    [Fact]
    public void ValidateElementsInYaml_PascalCase_EnumValueNameLowercase_Fail()
    {
        var schemaValidator = new CkSchemaValidator();

        var filePath = "sampleData/files/pascalCase-enumValueName-lowercase-fail.yaml";
        var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();
        var isValid = schemaValidator.ValidateElementsInYaml(stream, filePath, operationResult);
        Assert.False(isValid);
        Assert.True(operationResult.Messages.Any());
        Assert.True(operationResult.HasErrors);
        Assert.Equal(27, operationResult.Messages[0].MessageNumber);
        Assert.Contains("active", operationResult.Messages[0].MessageText);
    }

    #endregion
}