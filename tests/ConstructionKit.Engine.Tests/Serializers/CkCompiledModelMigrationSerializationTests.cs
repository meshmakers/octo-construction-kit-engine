using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

public class CkCompiledModelMigrationSerializationTests
{
    private readonly CkYamlSerializer _yamlSerializer = new(new CkSchemaValidator());
    private readonly CkJsonSerializer _jsonSerializer = new();

    private static CkCompiledModelRoot CreateCompiledModelWithMigrations()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-2.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")],
            Migrations = new CkCompiledMigrationDataDto
            {
                Meta = new CkMigrationMetaDto
                {
                    CkModelId = "Test-2.0.0",
                    Migrations =
                    [
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "1.0.0",
                            ToVersion = "2.0.0",
                            ScriptPath = "1-0-0-to-2-0-0.yaml",
                            Description = "Migrate from v1 to v2"
                        }
                    ]
                },
                Scripts =
                [
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "1.0.0",
                        TargetVersion = "2.0.0",
                        Description = "Rename attribute",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/MyType" },
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.RenameAttribute,
                                    SourceAttribute = "OldName",
                                    TargetAttribute = "NewName"
                                }
                            }
                        ]
                    }
                ]
            }
        };
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithMigrations_PreservesMigrationData()
    {
        var original = CreateCompiledModelWithMigrations();

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal("Test-2.0.0", deserialized.Migrations.Meta.CkModelId);
        Assert.Single(deserialized.Migrations.Meta.Migrations);
        Assert.Equal("1.0.0", deserialized.Migrations.Meta.Migrations[0].FromVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Meta.Migrations[0].ToVersion);
        Assert.Single(deserialized.Migrations.Scripts);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[0].TargetVersion);
        Assert.Single(deserialized.Migrations.Scripts[0].Steps);
        Assert.Equal("step-1", deserialized.Migrations.Scripts[0].Steps[0].StepId);
        Assert.Equal(CkMigrationTransformType.RenameAttribute,
            deserialized.Migrations.Scripts[0].Steps[0].Transform!.Type);
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithoutMigrations_MigrationsIsNull()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-1.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")]
        };

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Null(deserialized.Migrations);
    }

    [Fact]
    public async Task RoundTrip_Json_WithMigrations_PreservesMigrationData()
    {
        var original = CreateCompiledModelWithMigrations();

        // Serialize to JSON
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _jsonSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize from JSON
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _jsonSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal("Test-2.0.0", deserialized.Migrations.Meta.CkModelId);
        Assert.Single(deserialized.Migrations.Meta.Migrations);
        Assert.Equal("1.0.0", deserialized.Migrations.Meta.Migrations[0].FromVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Meta.Migrations[0].ToVersion);
        Assert.Single(deserialized.Migrations.Scripts);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[0].TargetVersion);
        Assert.Single(deserialized.Migrations.Scripts[0].Steps);
        Assert.Equal(CkMigrationTransformType.RenameAttribute,
            deserialized.Migrations.Scripts[0].Steps[0].Transform!.Type);
    }

    [Fact]
    public async Task RoundTrip_Json_WithoutMigrations_MigrationsIsNull()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-1.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")]
        };

        // Serialize to JSON
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _jsonSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize from JSON
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _jsonSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Null(deserialized.Migrations);
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithComplexMigrationStructures_PreservesAll()
    {
        // Test complex nested structures: filters with And/Or, preConditions, postValidations, valueMapping, data
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-2.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")],
            Migrations = new CkCompiledMigrationDataDto
            {
                Meta = new CkMigrationMetaDto
                {
                    CkModelId = "Test-2.0.0",
                    Migrations =
                    [
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "1.0.0",
                            ToVersion = "2.0.0",
                            ScriptPath = "1-0-0-to-2-0-0.yaml",
                            Description = "Complex migration",
                            Breaking = true
                        }
                    ]
                },
                Scripts =
                [
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "1.0.0",
                        TargetVersion = "2.0.0",
                        Description = "Complex migration script",
                        PreConditions =
                        [
                            new CkMigrationConditionDto
                            {
                                Type = CkMigrationConditionType.CkModelVersionInstalled,
                                CkModelId = "System",
                                Version = "1.0.0"
                            },
                            new CkMigrationConditionDto
                            {
                                Type = CkMigrationConditionType.EntityExists,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/OldType" }
                            }
                        ],
                        Steps =
                        [
                            // Step with filter using And/Or
                            new CkMigrationStepDto
                            {
                                StepId = "step-filter",
                                Action = CkMigrationActionType.Transform,
                                Target = new CkMigrationTargetDto
                                {
                                    CkTypeId = "${Test}/MyType",
                                    Filter = new CkMigrationFilterDto
                                    {
                                        And =
                                        [
                                            new CkMigrationFilterDto
                                            {
                                                Attribute = "Status",
                                                Operator = CkMigrationFilterOperator.Eq,
                                                Value = "Active"
                                            },
                                            new CkMigrationFilterDto
                                            {
                                                Or =
                                                [
                                                    new CkMigrationFilterDto
                                                    {
                                                        Attribute = "Category",
                                                        Operator = CkMigrationFilterOperator.Eq,
                                                        Value = "A"
                                                    },
                                                    new CkMigrationFilterDto
                                                    {
                                                        Attribute = "Category",
                                                        Operator = CkMigrationFilterOperator.Eq,
                                                        Value = "B"
                                                    }
                                                ]
                                            }
                                        ]
                                    }
                                },
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.MapValue,
                                    TargetAttribute = "Priority",
                                    ValueMapping = new Dictionary<string, object>
                                    {
                                        ["High"] = "Critical",
                                        ["Medium"] = "Normal",
                                        ["Low"] = "Minor"
                                    }
                                }
                            },
                            // Step with condition, onConflict, continueOnError
                            new CkMigrationStepDto
                            {
                                StepId = "step-conditional",
                                Description = "Conditional step with conflict handling",
                                Action = CkMigrationActionType.Update,
                                Target = new CkMigrationTargetDto
                                {
                                    CkTypeId = "${Test}/MyType",
                                    BlueprintSourceOnly = true
                                },
                                Data = new Dictionary<string, object>
                                {
                                    ["Status"] = "Migrated",
                                    ["Version"] = 2
                                },
                                Condition = new CkMigrationConditionDto
                                {
                                    Type = CkMigrationConditionType.AttributeEquals,
                                    Target = new CkMigrationTargetDto { CkTypeId = "${Test}/MyType" },
                                    Attribute = "Status",
                                    Value = "Active"
                                },
                                OnConflict = CkMigrationConflictBehavior.Skip,
                                ContinueOnError = true
                            },
                            // Step with ChangeCkType
                            new CkMigrationStepDto
                            {
                                StepId = "step-changetype",
                                Action = CkMigrationActionType.Transform,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/OldType" },
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.ChangeCkType,
                                    NewCkTypeId = "${Test}/NewType"
                                }
                            },
                            // Delete step
                            new CkMigrationStepDto
                            {
                                StepId = "step-delete",
                                Action = CkMigrationActionType.Delete,
                                Target = new CkMigrationTargetDto
                                {
                                    RtWellKnownName = "obsolete-entity"
                                }
                            }
                        ],
                        PostValidations =
                        [
                            new CkMigrationPostValidationDto
                            {
                                ValidationId = "val-1",
                                Description = "Ensure no old-type entities remain",
                                Type = CkMigrationValidationType.NoEntitiesOfType,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/OldType" },
                                Severity = CkMigrationValidationSeverity.Error
                            },
                            new CkMigrationPostValidationDto
                            {
                                ValidationId = "val-2",
                                Type = CkMigrationValidationType.EntityCount,
                                Target = new CkMigrationTargetDto { CkTypeId = "${Test}/NewType" },
                                ExpectedCount = 5,
                                Severity = CkMigrationValidationSeverity.Warning
                            }
                        ]
                    }
                ]
            }
        };

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var d = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors,
            $"Deserialization errors: {string.Join(", ", operationResult.Messages.Select(m => m.MessageText))}");

        // Verify meta
        Assert.NotNull(d.Migrations);
        Assert.True(d.Migrations.Meta.Migrations[0].Breaking);
        Assert.Equal("Complex migration", d.Migrations.Meta.Migrations[0].Description);

        // Verify script basics
        var script = d.Migrations.Scripts[0];
        Assert.Equal("Complex migration script", script.Description);

        // Verify preConditions
        Assert.NotNull(script.PreConditions);
        Assert.Equal(2, script.PreConditions.Count);
        Assert.Equal(CkMigrationConditionType.CkModelVersionInstalled, script.PreConditions[0].Type);
        Assert.Equal("System", script.PreConditions[0].CkModelId);
        Assert.Equal("1.0.0", script.PreConditions[0].Version);
        Assert.Equal(CkMigrationConditionType.EntityExists, script.PreConditions[1].Type);
        Assert.Equal("${Test}/OldType", script.PreConditions[1].Target!.CkTypeId);

        // Verify step with nested And/Or filters
        Assert.Equal(4, script.Steps.Count);
        var filterStep = script.Steps[0];
        Assert.NotNull(filterStep.Target!.Filter);
        Assert.NotNull(filterStep.Target.Filter.And);
        Assert.Equal(2, filterStep.Target.Filter.And.Count);
        Assert.Equal("Status", filterStep.Target.Filter.And[0].Attribute);
        Assert.Equal(CkMigrationFilterOperator.Eq, filterStep.Target.Filter.And[0].Operator);
        Assert.NotNull(filterStep.Target.Filter.And[1].Or);
        Assert.Equal(2, filterStep.Target.Filter.And[1].Or!.Count);

        // Verify MapValue transform with valueMapping
        Assert.Equal(CkMigrationTransformType.MapValue, filterStep.Transform!.Type);
        Assert.NotNull(filterStep.Transform.ValueMapping);
        Assert.Equal(3, filterStep.Transform.ValueMapping.Count);

        // Verify conditional step with data, onConflict, continueOnError
        var conditionalStep = script.Steps[1];
        Assert.Equal(CkMigrationActionType.Update, conditionalStep.Action);
        Assert.NotNull(conditionalStep.Data);
        Assert.Equal(2, conditionalStep.Data.Count);
        Assert.NotNull(conditionalStep.Condition);
        Assert.Equal(CkMigrationConditionType.AttributeEquals, conditionalStep.Condition.Type);
        Assert.Equal("Status", conditionalStep.Condition.Attribute);
        Assert.Equal(CkMigrationConflictBehavior.Skip, conditionalStep.OnConflict);
        Assert.True(conditionalStep.ContinueOnError);
        Assert.True(conditionalStep.Target!.BlueprintSourceOnly);

        // Verify ChangeCkType step
        var changeTypeStep = script.Steps[2];
        Assert.Equal(CkMigrationTransformType.ChangeCkType, changeTypeStep.Transform!.Type);
        Assert.Equal("${Test}/NewType", changeTypeStep.Transform.NewCkTypeId);

        // Verify Delete step with RtWellKnownName
        var deleteStep = script.Steps[3];
        Assert.Equal(CkMigrationActionType.Delete, deleteStep.Action);
        Assert.Equal("obsolete-entity", deleteStep.Target!.RtWellKnownName);

        // Verify postValidations
        Assert.NotNull(script.PostValidations);
        Assert.Equal(2, script.PostValidations.Count);
        Assert.Equal("val-1", script.PostValidations[0].ValidationId);
        Assert.Equal(CkMigrationValidationType.NoEntitiesOfType, script.PostValidations[0].Type);
        Assert.Equal(CkMigrationValidationSeverity.Error, script.PostValidations[0].Severity);
        Assert.Equal("val-2", script.PostValidations[1].ValidationId);
        Assert.Equal(CkMigrationValidationType.EntityCount, script.PostValidations[1].Type);
        Assert.Equal(5, script.PostValidations[1].ExpectedCount);
        Assert.Equal(CkMigrationValidationSeverity.Warning, script.PostValidations[1].Severity);
    }

    [Fact]
    public async Task RoundTrip_Yaml_WithMultipleMigrations_PreservesAll()
    {
        var original = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Test-3.0.0"),
            Dependencies = [new CkModelId("System-1.0.0")],
            Migrations = new CkCompiledMigrationDataDto
            {
                Meta = new CkMigrationMetaDto
                {
                    CkModelId = "Test-3.0.0",
                    Migrations =
                    [
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "1.0.0",
                            ToVersion = "3.0.0",
                            ScriptPath = "1-0-0-to-3-0-0.yaml"
                        },
                        new CkMigrationReferenceDto
                        {
                            FromVersion = "2.0.0",
                            ToVersion = "3.0.0",
                            ScriptPath = "2-0-0-to-3-0-0.yaml"
                        }
                    ]
                },
                Scripts =
                [
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "1.0.0",
                        TargetVersion = "3.0.0",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.ChangeCkType,
                                    NewCkTypeId = "${Test}/NewType"
                                }
                            }
                        ]
                    },
                    new CkMigrationScriptDto
                    {
                        SourceVersion = "2.0.0",
                        TargetVersion = "3.0.0",
                        Steps =
                        [
                            new CkMigrationStepDto
                            {
                                StepId = "step-1",
                                Action = CkMigrationActionType.Transform,
                                Transform = new CkMigrationTransformDto
                                {
                                    Type = CkMigrationTransformType.SetValue,
                                    TargetAttribute = "Status",
                                    Value = "Active"
                                }
                            }
                        ]
                    }
                ]
            }
        };

        // Serialize
        var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream);
        await _yamlSerializer.SerializeAsync(writer, original);
        await writer.FlushAsync(TestContext.Current.CancellationToken);

        // Deserialize
        stream.Position = 0;
        var operationResult = new OperationResult();
        var deserialized = await _yamlSerializer.DeserializeCompiledModelRootAsync(
            stream, "test.yaml", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.NotNull(deserialized.Migrations);
        Assert.Equal(2, deserialized.Migrations.Meta.Migrations.Count);
        Assert.Equal(2, deserialized.Migrations.Scripts.Count);
        Assert.Equal("1.0.0", deserialized.Migrations.Scripts[0].SourceVersion);
        Assert.Equal("2.0.0", deserialized.Migrations.Scripts[1].SourceVersion);
    }
}
