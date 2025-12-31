using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

public class BlueprintSchemaValidatorTests
{
    private readonly BlueprintSchemaValidator _validator = new();

    #region Meta Schema - YAML Tests

    [Fact]
    public void ValidateMetaInYaml_ValidBlueprint_ReturnsTrue()
    {
        var filePath = "sampleData/blueprints/blueprint-meta-valid.yaml";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInYaml(stream, filePath, operationResult);

        Assert.True(isValid);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateMetaInYaml_MissingBlueprintId_ReturnsFalse()
    {
        var filePath = "sampleData/blueprints/blueprint-meta-missing-id.yaml";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInYaml(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    #endregion

    #region Meta Schema - JSON Tests

    [Fact]
    public void ValidateMetaInJson_ValidBlueprint_ReturnsTrue()
    {
        var filePath = "sampleData/blueprints/blueprint-meta-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInJson(stream, filePath, operationResult);

        Assert.True(isValid);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public void ValidateMetaInJson_InvalidProperty_ReturnsFalse()
    {
        var filePath = "sampleData/blueprints/blueprint-meta-invalid-property.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInJson(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    #endregion

    #region Catalog Index Schema Tests

    [Fact]
    public void ValidateCatalogIndexInJson_ValidIndex_ReturnsTrue()
    {
        var filePath = "sampleData/blueprints/catalog-index-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateCatalogIndexInJson(stream, filePath, operationResult);

        Assert.True(isValid);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
    }

    [Fact]
    public void ValidateCatalogIndexInJson_MissingPath_ReturnsFalse()
    {
        var filePath = "sampleData/blueprints/catalog-index-invalid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateCatalogIndexInJson(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    #endregion

    #region Library Versions Schema Tests

    [Fact]
    public void ValidateLibraryVersionsInJson_ValidVersions_ReturnsTrue()
    {
        var filePath = "sampleData/blueprints/library-versions-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateLibraryVersionsInJson(stream, filePath, operationResult);

        Assert.True(isValid);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
    }

    [Fact]
    public void ValidateLibraryVersionsInJson_WithCatalogIndexFile_ReturnsFalse()
    {
        // Using a catalog index file for library versions validation should fail
        var filePath = "sampleData/blueprints/catalog-index-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateLibraryVersionsInJson(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    #endregion

    #region Cross-Schema Validation Tests

    [Fact]
    public void ValidateMetaInJson_WithCatalogIndexFile_ReturnsFalse()
    {
        // Using a catalog index file for meta validation should fail
        var filePath = "sampleData/blueprints/catalog-index-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInJson(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    [Fact]
    public void ValidateCatalogIndexInJson_WithMetaFile_ReturnsFalse()
    {
        // Using a meta file for catalog index validation should fail
        var filePath = "sampleData/blueprints/blueprint-meta-valid.json";
        using var stream = File.OpenRead(filePath);
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateCatalogIndexInJson(stream, filePath, operationResult);

        Assert.False(isValid);
        Assert.NotEmpty(operationResult.Messages);
        Assert.True(operationResult.HasErrors);
    }

    #endregion
}
