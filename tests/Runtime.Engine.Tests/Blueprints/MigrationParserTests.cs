using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

public class MigrationParserTests
{
    private readonly MigrationParser _sut;

    public MigrationParserTests()
    {
        _sut = new MigrationParser();
    }

    [Fact]
    public void Parse_ValidMinimalYaml_ShouldReturnMigration()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public void Parse_WithDescription_ShouldParseDescription()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "Migration from v1 to v2"
            steps: []
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Equal("Migration from v1 to v2", result.Description);
    }

    [Fact]
    public void Parse_WithAddStep_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: add-entity
                description: Add new dashboard entity
                action: Add
                target:
                  ckTypeId: System/Entity
                  rtWellKnownName: NewDashboard
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal("add-entity", step.StepId);
        Assert.Equal("Add new dashboard entity", step.Description);
        Assert.Equal(MigrationActionType.Add, step.Action);
        Assert.NotNull(step.Target);
        Assert.Equal("System/Entity", step.Target.CkTypeId);
        Assert.Equal("NewDashboard", step.Target.RtWellKnownName);
    }

    [Fact]
    public void Parse_WithUpdateStep_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-config
                action: Update
                target:
                  rtWellKnownName: MainConfig
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal("update-config", step.StepId);
        Assert.Equal(MigrationActionType.Update, step.Action);
        Assert.Equal("MainConfig", step.Target.RtWellKnownName);
    }

    [Fact]
    public void Parse_WithDeleteStep_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: remove-legacy
                action: Delete
                target:
                  rtWellKnownName: LegacyConfig
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal(MigrationActionType.Delete, step.Action);
        Assert.Equal("LegacyConfig", step.Target.RtWellKnownName);
    }

    [Fact]
    public void Parse_WithTransformStep_ShouldParseStep()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: rename-attribute
                action: Transform
                target:
                  ckTypeId: System/Entity
                transform:
                  type: Rename
                  sourceAttribute: OldName
                  targetAttribute: NewName
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Single(result.Steps);
        var step = result.Steps[0];
        Assert.Equal(MigrationActionType.Transform, step.Action);
        Assert.NotNull(step.Transform);
        Assert.Equal(TransformType.Rename, step.Transform.Type);
        Assert.Equal("OldName", step.Transform.SourceAttribute);
        Assert.Equal("NewName", step.Transform.TargetAttribute);
    }

    [Fact]
    public void Parse_WithConflictBehavior_ShouldParseConflictBehavior()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: add-entity
                action: Add
                target:
                  ckTypeId: System/Entity
                onConflict: Skip
                continueOnError: true
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.Equal(MigrationConflictBehavior.Skip, step.OnConflict);
        Assert.True(step.ContinueOnError);
    }

    [Fact]
    public void Parse_WithFilter_ShouldParseFilter()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-entities
                action: Update
                target:
                  ckTypeId: System/Entity
                  filter:
                    attribute: Status
                    operator: Eq
                    value: Active
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Target.Filter);
        Assert.Equal("Status", step.Target.Filter.Attribute);
        Assert.Equal(FilterOperator.Eq, step.Target.Filter.Operator);
        Assert.Equal("Active", step.Target.Filter.Value);
    }

    [Fact]
    public void Parse_WithPreConditions_ShouldParsePreConditions()
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
        var result = _sut.Parse(yaml);

        // Assert
        Assert.NotNull(result.PreConditions);
        Assert.Single(result.PreConditions);
        var condition = result.PreConditions[0];
        Assert.Equal(MigrationConditionType.EntityExists, condition.Type);
        Assert.NotNull(condition.Target);
        Assert.Equal("RequiredEntity", condition.Target.RtWellKnownName);
    }

    [Fact]
    public void Parse_WithPostValidations_ShouldParsePostValidations()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            postValidations:
              - validationId: check-entity-count
                description: Verify entity count
                type: EntityCount
                target:
                  ckTypeId: System/Entity
                expectedCount: 10
                severity: Warning
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.NotNull(result.PostValidations);
        Assert.Single(result.PostValidations);
        var validation = result.PostValidations[0];
        Assert.Equal("check-entity-count", validation.ValidationId);
        Assert.Equal("Verify entity count", validation.Description);
        Assert.Equal(MigrationValidationType.EntityCount, validation.Type);
        Assert.Equal(10, validation.ExpectedCount);
        Assert.Equal(MigrationValidationSeverity.Warning, validation.Severity);
    }

    [Fact]
    public void Parse_WithMultipleSteps_ShouldParseAllSteps()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: step-1
                action: Add
                target:
                  rtWellKnownName: Entity1
              - stepId: step-2
                action: Update
                target:
                  rtWellKnownName: Entity2
              - stepId: step-3
                action: Delete
                target:
                  rtWellKnownName: Entity3
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("step-1", result.Steps[0].StepId);
        Assert.Equal("step-2", result.Steps[1].StepId);
        Assert.Equal("step-3", result.Steps[2].StepId);
    }

    [Fact]
    public void Parse_EmptyYaml_ShouldThrowArgumentException()
    {
        // Arrange
        var yaml = "";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.Parse(yaml));
    }

    [Fact]
    public void Parse_WhitespaceOnlyYaml_ShouldThrowArgumentException()
    {
        // Arrange
        var yaml = "   \n   \t   ";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _sut.Parse(yaml));
    }

    [Fact]
    public void Parse_MissingSourceVersion_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            targetVersion: "2.0.0"
            steps: []
            """;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.Parse(yaml));
        Assert.Contains("sourceVersion", exception.Message);
    }

    [Fact]
    public void Parse_MissingTargetVersion_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            steps: []
            """;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _sut.Parse(yaml));
        Assert.Contains("targetVersion", exception.Message);
    }

    [Fact]
    public void Parse_InvalidYaml_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - this is invalid yaml
                not properly formatted
            """;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _sut.Parse(yaml));
    }

    [Fact]
    public async Task ParseAsync_NonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var filePath = "/non/existent/path/migration.yaml";
        var ct = TestContext.Current.CancellationToken;

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.ParseAsync(filePath, ct));
    }

    [Fact]
    public void Parse_WithBlueprintSourceOnly_ShouldParseBlueprintSourceOnly()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-entities
                action: Update
                target:
                  ckTypeId: System/Entity
                  blueprintSourceOnly: false
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.False(step.Target.BlueprintSourceOnly);
    }

    [Fact]
    public void Parse_DefaultBlueprintSourceOnly_ShouldBeTrue()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: update-entities
                action: Update
                target:
                  ckTypeId: System/Entity
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.True(step.Target.BlueprintSourceOnly);
    }

    [Fact]
    public void Parse_WithSetValueTransform_ShouldParseValue()
    {
        // Arrange
        var yaml = """
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps:
              - stepId: set-version
                action: Transform
                target:
                  ckTypeId: System/Entity
                transform:
                  type: SetValue
                  targetAttribute: Version
                  value: "2.0"
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        var step = result.Steps[0];
        Assert.NotNull(step.Transform);
        Assert.Equal(TransformType.SetValue, step.Transform.Type);
        Assert.Equal("Version", step.Transform.TargetAttribute);
        Assert.Equal("2.0", step.Transform.Value);
    }

    [Fact]
    public void Parse_WithSchemaUri_ShouldIgnoreSchemaUri()
    {
        // Arrange
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-migration.schema.json
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            steps: []
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.SourceVersion);
        Assert.Equal("2.0.0", result.TargetVersion);
    }

    [Fact]
    public void Parse_ComplexMigration_ShouldParseAllElements()
    {
        // Arrange
        var yaml = """
            $schema: https://schemas.meshmakers.cloud/blueprint-migration.schema.json
            sourceVersion: "1.0.0"
            targetVersion: "2.0.0"
            description: "Complete migration from v1 to v2"
            preConditions:
              - type: EntityExists
                target:
                  rtWellKnownName: Configuration
            steps:
              - stepId: add-dashboard
                description: Add new monitoring dashboard
                action: Add
                target:
                  ckTypeId: Monitoring/Dashboard
                  rtWellKnownName: MainDashboard
              - stepId: update-config
                description: Enable monitoring in config
                action: Update
                target:
                  rtWellKnownName: Configuration
                onConflict: Merge
              - stepId: remove-legacy
                description: Remove deprecated legacy entity
                action: Delete
                target:
                  rtWellKnownName: LegacyMonitor
                continueOnError: true
            postValidations:
              - validationId: verify-dashboard
                type: EntityExists
                target:
                  rtWellKnownName: MainDashboard
            """;

        // Act
        var result = _sut.Parse(yaml);

        // Assert
        Assert.Equal("Complete migration from v1 to v2", result.Description);
        Assert.NotNull(result.PreConditions);
        Assert.Single(result.PreConditions);
        Assert.Equal(3, result.Steps.Count);
        Assert.NotNull(result.PostValidations);
        Assert.Single(result.PostValidations);
    }
}
