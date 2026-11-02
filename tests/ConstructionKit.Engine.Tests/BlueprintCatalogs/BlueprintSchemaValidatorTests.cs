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

    [Fact]
    public void ValidateMetaInYaml_ArchiveActivationExclude_ReturnsTrue()
    {
        // AB#7991: additionalProperties:false - the opt-out block must be declared or every blueprint using it fails.
        const string yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            archiveActivation:
              exclude:
                - 6a0e000000000000000a0007
                - GridConnectionRollup1h
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var operationResult = new OperationResult();

        Assert.True(_validator.ValidateMetaInYaml(stream, "test.yaml", operationResult));
        Assert.Empty(operationResult.Messages);
    }

    [Theory]
    [InlineData("archiveActivation:\n  skip:\n    - A")]
    [InlineData("archiveActivation:\n  exclude:\n    - \"\"")]
    [InlineData("archiveActivation:\n  exclude: GridArchive")]
    public void ValidateMetaInYaml_InvalidArchiveActivation_ReturnsFalse(string block)
    {
        var yaml = "blueprintId: TestBlueprint-1.0.0\n" + block + "\n";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var operationResult = new OperationResult();

        Assert.False(_validator.ValidateMetaInYaml(stream, "test.yaml", operationResult));
    }

    [Fact]
    public void ValidateMetaInYaml_SplitSeedDataPaths_ReturnsTrue()
    {
        // The meta schema sets additionalProperties:false, so seedDataPaths (AB#4758) has to be
        // declared there or every blueprint using it fails the build-time BlueprintEmbed gate.
        const string yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
            blueprintId: TestBlueprint-1.0.0
            seedDataPaths:
              - seed-data/configurations/base.yaml
              - seed-data/data-flows/camt053.yaml
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var operationResult = new OperationResult();

        var isValid = _validator.ValidateMetaInYaml(stream, "test.yaml", operationResult);

        Assert.True(isValid);
        Assert.Empty(operationResult.Messages);
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
