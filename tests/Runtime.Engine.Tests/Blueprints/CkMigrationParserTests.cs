using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

public class CkMigrationParserTests
{
    private readonly CkMigrationParser _sut;

    public CkMigrationParserTests()
    {
        _sut = new CkMigrationParser();
    }

    #region ParseMeta Tests

    [Fact]
    public void ParseMeta_ValidMinimalYaml_ShouldReturnMeta()
    {
        // Arrange
        var yaml = """
            ckModelId: "MyModel-1.0.0"
            migrations: []
            """;

        // Act
        var result = _sut.ParseMeta(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MyModel-1.0.0", result.CkModelId);
        Assert.Empty(result.Migrations);
    }

    [Fact]
    public void ParseMeta_WithMigrations_ShouldParseMigrations()
    {
        // Arrange
        var yaml = """
            ckModelId: "MyModel-2.0.0"
            migrations:
              - fromVersion: "1.0.0"
                toVersion: "2.0.0"
                scriptPath: "1.0.0-to-2.0.0.yaml"
                description: "Migrate from v1 to v2"
                breaking: true
              - fromVersion: "1.5.0"
                toVersion: "2.0.0"
                scriptPath: "1.5.0-to-2.0.0.yaml"
            """;

        // Act
        var result = _sut.ParseMeta(yaml);

        // Assert
        Assert.Equal(2, result.Migrations.Count);

        var firstMigration = result.Migrations[0];
        Assert.Equal("1.0.0", firstMigration.FromVersion);
        Assert.Equal("2.0.0", firstMigration.ToVersion);
        Assert.Equal("1.0.0-to-2.0.0.yaml", firstMigration.ScriptPath);
        Assert.Equal("Migrate from v1 to v2", firstMigration.Description);
        Assert.True(firstMigration.Breaking);

        var secondMigration = result.Migrations[1];
        Assert.Equal("1.5.0", secondMigration.FromVersion);
        Assert.Equal("2.0.0", secondMigration.ToVersion);
        Assert.False(secondMigration.Breaking);
    }

    [Fact]
    public void ParseMeta_WithSchemaUri_ShouldIgnoreSchemaUri()
    {
        // Arrange
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/ck-migration-meta.schema.json
            ckModelId: "MyModel-1.0.0"
            migrations: []
            """;

        // Act
        var result = _sut.ParseMeta(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MyModel-1.0.0", result.CkModelId);
    }

    [Fact]
    public void ParseMeta_EmptyYaml_ShouldThrowArgumentException()
    {
        // Arrange
        var yaml = "";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.ParseMeta(yaml));
    }

    [Fact]
    public void ParseMeta_WhitespaceOnlyYaml_ShouldThrowArgumentException()
    {
        // Arrange
        var yaml = "   \n   \t   ";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.ParseMeta(yaml));
    }

    [Fact]
    public void ParseMeta_MissingCkModelId_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            migrations: []
            """;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.ParseMeta(yaml));
        Assert.Contains("ckModelId", exception.Message);
    }

    [Fact]
    public void ParseMeta_InvalidYaml_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            ckModelId: "MyModel-1.0.0"
            migrations:
              - this is invalid yaml
                not properly formatted
            """;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _sut.ParseMeta(yaml));
    }

    #endregion

    #region ParseScript Tests

    [Fact]
    public void ParseScript_ValidMinimalYaml_ShouldReturnScript()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void ParseScript_WithDescription_ShouldParseDescription()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "CK Migration from v1 to v2"
            steps: []
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.Equal("CK Migration from v1 to v2", result.Description);
    }

    [Fact]
    public void ParseScript_WithTransformStep_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: rename-attribute
                description: Rename oldName to newName
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                transform:
                  type: RenameAttribute
                  sourceAttribute: oldName
                  targetAttribute: newName
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal("rename-attribute", step.StepId);
        Assert.Equal("Rename oldName to newName", step.Description);
        Assert.Equal(CkMigrationActionType.Transform, step.Action);
        Assert.NotNull(step.Target);
        Assert.Equal("MyModel/Entity", step.Target.CkTypeId);
        Assert.NotNull(step.Transform);
        Assert.Equal(CkMigrationTransformType.RenameAttribute, step.Transform.Type);
        Assert.Equal("oldName", step.Transform.SourceAttribute);
        Assert.Equal("newName", step.Transform.TargetAttribute);
    }

    [Fact]
    public void ParseScript_WithSetValueTransform_ShouldParseValue()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: set-default
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                transform:
                  type: SetValue
                  targetAttribute: status
                  value: "active"
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Transform);
        Assert.Equal(CkMigrationTransformType.SetValue, step.Transform.Type);
        Assert.Equal("status", step.Transform.TargetAttribute);
        Assert.Equal("active", step.Transform.Value);
    }

    [Fact]
    public void ParseScript_WithMapValueTransform_ShouldParseValueMapping()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: map-status
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                transform:
                  type: MapValue
                  targetAttribute: status
                  valueMapping:
                    "0": "inactive"
                    "1": "active"
                    "2": "archived"
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Transform);
        Assert.Equal(CkMigrationTransformType.MapValue, step.Transform.Type);
        Assert.NotNull(step.Transform.ValueMapping);
        Assert.Equal(3, step.Transform.ValueMapping.Count);
    }

    [Fact]
    public void ParseScript_WithChangeCkTypeTransform_ShouldParseNewCkTypeId()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: change-type
                action: Transform
                target:
                  ckTypeId: MyModel/OldType
                transform:
                  type: ChangeCkType
                  newCkTypeId: MyModel/NewType
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Transform);
        Assert.Equal(CkMigrationTransformType.ChangeCkType, step.Transform.Type);
        Assert.Equal("MyModel/NewType", step.Transform.NewCkTypeId);
    }

    [Fact]
    public void ParseScript_WithAddAction_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: add-config
                action: Add
                target:
                  ckTypeId: MyModel/Configuration
                  rtWellKnownName: DefaultConfig
                data:
                  name: "Default Configuration"
                  version: "2.0"
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.Equal(CkMigrationActionType.Add, step.Action);
        Assert.Equal("DefaultConfig", step.Target?.RtWellKnownName);
        Assert.NotNull(step.Data);
        Assert.Equal(2, step.Data.Count);
    }

    [Fact]
    public void ParseScript_WithDeleteAction_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: remove-deprecated
                action: Delete
                target:
                  ckTypeId: MyModel/DeprecatedType
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.Equal(CkMigrationActionType.Delete, step.Action);
        Assert.Equal("MyModel/DeprecatedType", step.Target?.CkTypeId);
    }

    [Fact]
    public void ParseScript_WithFilter_ShouldParseFilter()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-filtered
                action: Update
                target:
                  ckTypeId: MyModel/Entity
                  filter:
                    attribute: status
                    operator: Eq
                    value: draft
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Target?.Filter);
        Assert.Equal("status", step.Target.Filter.Attribute);
        Assert.Equal(CkMigrationFilterOperator.Eq, step.Target.Filter.Operator);
        Assert.Equal("draft", step.Target.Filter.Value);
    }

    [Fact]
    public void ParseScript_WithAndFilter_ShouldParseNestedFilters()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-filtered
                action: Update
                target:
                  ckTypeId: MyModel/Entity
                  filter:
                    and:
                      - attribute: status
                        operator: Eq
                        value: draft
                      - attribute: priority
                        operator: Eq
                        value: high
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Target?.Filter);
        Assert.NotNull(step.Target.Filter.And);
        Assert.Equal(2, step.Target.Filter.And.Count);
    }

    [Fact]
    public void ParseScript_WithPreConditions_ShouldParsePreConditions()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            preConditions:
              - type: EntityExists
                target:
                  rtWellKnownName: RequiredEntity
            steps: []
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.NotNull(result.PreConditions);
        Assert.Single(result.PreConditions);
        Assert.Equal(CkMigrationConditionType.EntityExists, result.PreConditions[0].Type);
    }

    [Fact]
    public void ParseScript_WithPostValidations_ShouldParsePostValidations()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            postValidations:
              - validationId: check-migration
                type: EntityCount
                target:
                  ckTypeId: MyModel/Entity
                expectedCount: 10
                severity: Warning
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.NotNull(result.PostValidations);
        Assert.Single(result.PostValidations);
        var validation = result.PostValidations[0];
        Assert.Equal("check-migration", validation.ValidationId);
        Assert.Equal(CkMigrationValidationType.EntityCount, validation.Type);
        Assert.Equal(10, validation.ExpectedCount);
        Assert.Equal(CkMigrationValidationSeverity.Warning, validation.Severity);
    }

    [Fact]
    public void ParseScript_WithMultipleSteps_ShouldParseAllSteps()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: step-1
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                transform:
                  type: RenameAttribute
                  sourceAttribute: old
                  targetAttribute: new
              - stepId: step-2
                action: Add
                target:
                  ckTypeId: MyModel/Config
              - stepId: step-3
                action: Delete
                target:
                  ckTypeId: MyModel/Deprecated
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("step-1", result.Steps[0].StepId);
        Assert.Equal("step-2", result.Steps[1].StepId);
        Assert.Equal("step-3", result.Steps[2].StepId);
    }

    [Fact]
    public void ParseScript_EmptyYaml_ShouldThrowArgumentException()
    {
        // Arrange
        var yaml = "";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.ParseScript(yaml));
    }

    [Fact]
    public void ParseScript_MissingSourceVersion_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            targetVersion: "2.0.0"
            steps: []
            """;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.ParseScript(yaml));
        Assert.Contains("sourceVersion", exception.Message);
    }

    [Fact]
    public void ParseScript_MissingTargetVersion_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            steps: []
            """;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.ParseScript(yaml));
        Assert.Contains("targetVersion", exception.Message);
    }

    #endregion

    #region Stream Tests

    [Fact]
    public async Task ParseMetaFromStreamAsync_ValidStream_ShouldReturnMeta()
    {
        // Arrange
        var yaml = """
            ckModelId: "MyModel-1.0.0"
            migrations: []
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ParseMetaFromStreamAsync(stream, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MyModel-1.0.0", result.CkModelId);
    }

    [Fact]
    public async Task ParseScriptFromStreamAsync_ValidStream_ShouldReturnScript()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var ct = TestContext.Current.CancellationToken;

        // Act
        var result = await _sut.ParseScriptFromStreamAsync(stream, ct);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
    }

    [Fact]
    public async Task ParseMetaFromStreamAsync_StreamLeftOpen_ShouldLeaveStreamOpen()
    {
        // Arrange
        var yaml = """
            ckModelId: "MyModel-1.0.0"
            migrations: []
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        var ct = TestContext.Current.CancellationToken;

        // Act
        await _sut.ParseMetaFromStreamAsync(stream, ct);

        // Assert - stream should still be accessible
        Assert.True(stream.CanRead);
    }

    #endregion

    #region File Tests

    [Fact]
    public async Task ParseMetaAsync_NonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var filePath = "/non/existent/path/migration-meta.yaml";
        var ct = TestContext.Current.CancellationToken;

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.ParseMetaAsync(filePath, ct));
    }

    [Fact]
    public async Task ParseScriptAsync_NonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var filePath = "/non/existent/path/migration-script.yaml";
        var ct = TestContext.Current.CancellationToken;

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.ParseScriptAsync(filePath, ct));
    }

    #endregion

    #region Complex Migration Tests

    [Fact]
    public void ParseScript_ComplexMigration_ShouldParseAllElements()
    {
        // Arrange
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/ck-migration.schema.json
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "Complete CK model migration from v1 to v2"
            preConditions:
              - type: EntityExists
                target:
                  rtWellKnownName: Configuration
            steps:
              - stepId: rename-attribute
                description: Rename old attribute
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                transform:
                  type: RenameAttribute
                  sourceAttribute: oldName
                  targetAttribute: newName
              - stepId: set-default-status
                description: Set default status for entities
                action: Transform
                target:
                  ckTypeId: MyModel/Entity
                  filter:
                    attribute: status
                    operator: NotExists
                transform:
                  type: SetValue
                  targetAttribute: status
                  value: "active"
              - stepId: add-new-config
                description: Add new configuration entity
                action: Add
                target:
                  ckTypeId: MyModel/Configuration
                  rtWellKnownName: NewConfig
                data:
                  name: "New Configuration"
                  version: "2.0"
                continueOnError: true
              - stepId: remove-deprecated
                description: Remove deprecated entities
                action: Delete
                target:
                  ckTypeId: MyModel/DeprecatedType
            postValidations:
              - validationId: verify-migration
                type: EntityExists
                target:
                  rtWellKnownName: NewConfig
            """;

        // Act
        var result = _sut.ParseScript(yaml);

        // Assert
        Assert.Equal("Complete CK model migration from v1 to v2", result.Description);
        Assert.NotNull(result.PreConditions);
        Assert.Single(result.PreConditions);
        Assert.Equal(4, result.Steps.Count);
        Assert.NotNull(result.PostValidations);
        Assert.Single(result.PostValidations);

        // Verify step details
        Assert.Equal(CkMigrationActionType.Transform, result.Steps[0].Action);
        Assert.Equal(CkMigrationActionType.Transform, result.Steps[1].Action);
        Assert.Equal(CkMigrationActionType.Add, result.Steps[2].Action);
        Assert.True(result.Steps[2].ContinueOnError);
        Assert.Equal(CkMigrationActionType.Delete, result.Steps[3].Action);
    }

    #endregion
}
