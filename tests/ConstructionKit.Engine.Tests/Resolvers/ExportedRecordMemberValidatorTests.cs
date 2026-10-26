using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#7467 (follow-up of AB#7446): message 138 <c>ExportedRecordDropsRequiredMember</c>. The shape of the
///     defect is <c>System.Communication</c> 4.10.0: a SeedOwned <c>RecordArray</c> of <c>DataPoint</c> whose required
///     member <c>ExternalId</c> was <c>RuntimeState</c>, so <c>ExportRt</c> dropped it.
/// </summary>
public class ExportedRecordMemberValidatorTests
{
    private const string Dep = "dep";
    private const string Model = "loxone";

    /// <summary>A copy of <c>DataPoint</c> 4.10.0 in a dependency model plus a nested record to test depth.</summary>
    private static CkCompiledModelRoot CreateDependency()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(Dep, "1.0.0"),
            Dependencies = [new("System", "2.5.0")],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
                new()
                {
                    AttributeId = "ExternalId", ValueType = AttributeValueTypesDto.String,
                    Ownership = AttributeOwnershipDto.RuntimeState
                },
                new()
                {
                    AttributeId = "CurrentValue", ValueType = AttributeValueTypesDto.String,
                    Ownership = AttributeOwnershipDto.RuntimeState
                },
                new()
                {
                    AttributeId = "Quality", ValueType = AttributeValueTypesDto.String,
                    Ownership = AttributeOwnershipDto.RuntimeState, DefaultValues = ["Good"]
                },
                new()
                {
                    AttributeId = "Inner", ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = $"{Dep}/InnerRecord"
                },
                new()
                {
                    AttributeId = "MappingTargets", ValueType = AttributeValueTypesDto.RecordArray,
                    ValueCkRecordId = $"{Dep}/MappingTarget", Ownership = AttributeOwnershipDto.RuntimeState
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "DataPoint",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{Dep}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{Dep}/ExternalId", AttributeName = "ExternalId" },
                        new() { CkAttributeId = $"{Dep}/CurrentValue", AttributeName = "CurrentValue", IsOptional = true },
                        new() { CkAttributeId = $"{Dep}/Quality", AttributeName = "Quality" }
                    ]
                },
                new()
                {
                    RecordId = "MappingTarget",
                    Attributes = [new() { CkAttributeId = $"{Dep}/ExternalId", AttributeName = "TargetId" }]
                },
                new()
                {
                    RecordId = "OuterRecord",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{Dep}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{Dep}/Inner", AttributeName = "Inner" }
                    ]
                },
                new()
                {
                    RecordId = "InnerRecord",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{Dep}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{Dep}/ExternalId", AttributeName = "ExternalId" }
                    ]
                }
            ]
        };
    }

    private static CkCompiledModelRoot CreateModel(string recordId = "DataPoint",
        AttributeValueTypesDto valueType = AttributeValueTypesDto.RecordArray,
        AttributeOwnershipDto? definitionOwnership = null, AttributeOwnershipDto? assignmentOwnership = null)
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(Model, "1.0.0"),
            Dependencies = [new("System", "2.5.0"), new(Dep, "1.0.0")],
            Attributes =
            [
                new()
                {
                    AttributeId = "States", ValueType = valueType, ValueCkRecordId = $"{Dep}/{recordId}",
                    Ownership = definitionOwnership
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Control",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new()
                        {
                            CkAttributeId = $"{Model}/States", AttributeName = "States", IsOptional = true,
                            Ownership = assignmentOwnership
                        }
                    ]
                }
            ]
        };
    }

    private static OperationResult Validate(CkCompiledModelRoot model, bool withDependency = true)
    {
        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        if (withDependency)
        {
            graph.AppendModel(CreateDependency());
        }

        graph.AppendModel(model);

        var result = new OperationResult();
        var origin = new OriginFileResolver("TEST");
        new InheritanceResolver(NullLogger<InheritanceResolver>.Instance).Resolve(graph, origin, result);
        ExportedRecordMemberValidator.Validate(model, graph, origin, result);
        return result;
    }

    private static List<OperationMessage> Hits(OperationResult result)
    {
        return result.Messages.Where(m => m.MessageNumber == 138).ToList();
    }

    [Fact]
    public void SeedOwnedRecordArray_WithRequiredRuntimeStateMember_WarnsOncePerMember()
    {
        // AB#7446 shape: SeedOwned RecordArray of a record whose required member ExternalId is RuntimeState.
        var result = Validate(CreateModel());

        var message = Assert.Single(Hits(result));
        Assert.Equal(MessageLevel.Warning, message.MessageLevel);
        Assert.False(result.HasErrors);
        Assert.Equal($"{Model}-1.0.0/Control-1", message.Parameters["ckElementId"]);
        Assert.Equal("States", message.Parameters["attributeName"]);
        Assert.Equal($"{Dep}-1.0.0/DataPoint-1", message.Parameters["ckRecordId"]);
        Assert.Equal("States.ExternalId", message.Parameters["memberPath"]);
        Assert.Equal("RuntimeState", message.Parameters["memberOwnership"]);
        Assert.Contains("States.ExternalId", message.MessageText);
    }

    [Fact]
    public void SingleRecordAttribute_IsWalkedLikeARecordArray()
    {
        var result = Validate(CreateModel(valueType: AttributeValueTypesDto.Record));

        Assert.Equal("States.ExternalId", Assert.Single(Hits(result)).Parameters["memberPath"]);
    }

    [Fact]
    public void TenantOwnedRecordAttribute_Warns()
    {
        var result = Validate(CreateModel(definitionOwnership: AttributeOwnershipDto.TenantOwned));

        Assert.Single(Hits(result));
    }

    [Fact]
    public void RecordDeclaredInTheCompiledModel_IsWalkedToo()
    {
        var model = CreateModel(recordId: "Local");
        model.Attributes!.Add(new() { AttributeId = "Secretive", ValueType = AttributeValueTypesDto.String, Ownership = AttributeOwnershipDto.RuntimeState });
        model.Records =
        [
            new()
            {
                RecordId = "Local",
                Attributes = [new() { CkAttributeId = $"{Model}/Secretive", AttributeName = "Secretive" }]
            }
        ];
        model.Attributes[0].ValueCkRecordId = $"{Model}/Local";

        var result = Validate(model, withDependency: false);

        var message = Assert.Single(Hits(result));
        Assert.Equal("States.Secretive", message.Parameters["memberPath"]);
        Assert.Equal($"{Model}-1.0.0/Local-1", message.Parameters["ckRecordId"]);
    }

    [Fact]
    public void RecordAttributeOfARecord_IsChecked_AndNamesTheDeclaringRecord()
    {
        var model = CreateModel();
        model.Records =
        [
            new()
            {
                RecordId = "Holder",
                Attributes = [new() { CkAttributeId = $"{Model}/States", AttributeName = "States" }]
            }
        ];

        var result = Validate(model);

        Assert.Contains(Hits(result), m => m.Parameters["ckElementId"] == $"{Model}-1.0.0/Holder-1");
    }

    [Fact]
    public void NestedRecord_ReportsFullMemberPath()
    {
        var result = Validate(CreateModel(recordId: "OuterRecord", valueType: AttributeValueTypesDto.Record));

        var message = Assert.Single(Hits(result));
        Assert.Equal("States.Inner.ExternalId", message.Parameters["memberPath"]);
        Assert.Equal($"{Dep}-1.0.0/InnerRecord-1", message.Parameters["ckRecordId"]);
    }

    [Fact]
    public void RecordAttributeOwnershipOverride_ToSeedOwned_Silences()
    {
        // Override on the record's own attribute assignment (dependency model copy with ExternalId made SeedOwned).
        var dependency = CreateDependency();
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Single(a => a.AttributeName == "ExternalId").Ownership = AttributeOwnershipDto.SeedOwned;

        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        graph.AppendModel(dependency);
        var model = CreateModel();
        graph.AppendModel(model);
        var result = new OperationResult();
        var origin = new OriginFileResolver("TEST");
        new InheritanceResolver(NullLogger<InheritanceResolver>.Instance).Resolve(graph, origin, result);
        ExportedRecordMemberValidator.Validate(model, graph, origin, result);

        Assert.Empty(Hits(result));
    }

    [Fact]
    public void RecordAttributeOwnershipOverride_ToTenantOwned_Silences()
    {
        var dependency = CreateDependency();
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Single(a => a.AttributeName == "ExternalId").Ownership = AttributeOwnershipDto.TenantOwned;

        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        graph.AppendModel(dependency);
        var model = CreateModel();
        graph.AppendModel(model);
        var result = new OperationResult();
        var origin = new OriginFileResolver("TEST");
        new InheritanceResolver(NullLogger<InheritanceResolver>.Instance).Resolve(graph, origin, result);
        ExportedRecordMemberValidator.Validate(model, graph, origin, result);

        Assert.Empty(Hits(result));
    }

    [Fact]
    public void TypeAttributeOverride_ToRuntimeState_Silences()
    {
        var result = Validate(CreateModel(assignmentOwnership: AttributeOwnershipDto.RuntimeState));

        Assert.Empty(Hits(result));
    }

    [Fact]
    public void RuntimeStateDefinition_OfTheRecordValuedAttribute_Silences()
    {
        var result = Validate(CreateModel(definitionOwnership: AttributeOwnershipDto.RuntimeState));

        Assert.Empty(Hits(result));
    }

    [Fact]
    public void TypeAttributeOverride_ToSeedOwned_OnRuntimeStateDefinition_Warns()
    {
        var result = Validate(CreateModel(definitionOwnership: AttributeOwnershipDto.RuntimeState,
            assignmentOwnership: AttributeOwnershipDto.SeedOwned));

        Assert.Single(Hits(result));
    }

    [Fact]
    public void OptionalAndDefaultedMembers_DoNotWarn()
    {
        // CurrentValue is optional + RuntimeState, Quality is required + RuntimeState with a default value: only
        // ExternalId is reported.
        var result = Validate(CreateModel());

        var message = Assert.Single(Hits(result));
        Assert.DoesNotContain("CurrentValue", message.Parameters["memberPath"]);
        Assert.DoesNotContain("Quality", message.Parameters["memberPath"]);
    }

    [Fact]
    public void RecordBehindARuntimeStateRecordMember_IsSkippedAsOneUnit()
    {
        // MappingTarget has a required RuntimeState member too, but the record is only reachable through an excluded
        // attribute (MappingTargets), which is dropped as a whole.
        var dependency = CreateDependency();
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Single(a => a.AttributeName == "ExternalId").Ownership = AttributeOwnershipDto.SeedOwned;
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Add(new() { CkAttributeId = $"{Dep}/MappingTargets", AttributeName = "MappingTargets", IsOptional = true });

        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        graph.AppendModel(dependency);
        var model = CreateModel();
        graph.AppendModel(model);
        var result = new OperationResult();
        var origin = new OriginFileResolver("TEST");
        new InheritanceResolver(NullLogger<InheritanceResolver>.Instance).Resolve(graph, origin, result);
        ExportedRecordMemberValidator.Validate(model, graph, origin, result);

        Assert.Empty(Hits(result));
    }

    [Fact]
    public void RequiredMember_OfSecretValueType_Warns()
    {
        var dependency = CreateDependency();
        dependency.Attributes!.Add(new() { AttributeId = "Token", ValueType = AttributeValueTypesDto.Secret });
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Single(a => a.AttributeName == "ExternalId").Ownership = AttributeOwnershipDto.SeedOwned;
        dependency.Records!.Single(r => r.RecordId.Name == "DataPoint").Attributes!
            .Add(new() { CkAttributeId = $"{Dep}/Token", AttributeName = "Token" });

        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        graph.AppendModel(dependency);
        var model = CreateModel();
        graph.AppendModel(model);
        var result = new OperationResult();
        var origin = new OriginFileResolver("TEST");
        new InheritanceResolver(NullLogger<InheritanceResolver>.Instance).Resolve(graph, origin, result);
        ExportedRecordMemberValidator.Validate(model, graph, origin, result);

        var message = Assert.Single(Hits(result));
        Assert.Equal("States.Token", message.Parameters["memberPath"]);
        Assert.Equal("Secret", message.Parameters["memberOwnership"]);
    }

    [Fact]
    public void ModelWithoutRecordAttributes_ProducesNothing()
    {
        var model = new CkCompiledModelRoot
        {
            ModelId = new CkModelId(Model, "1.0.0"),
            Dependencies = [new("System", "2.5.0")],
            Attributes = [new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String }],
            Types =
            [
                new()
                {
                    TypeId = "Plain", DerivedFromCkTypeId = "System/Entity",
                    Attributes = [new() { CkAttributeId = $"{Model}/Name", AttributeName = "Name" }]
                }
            ]
        };

        Assert.Empty(Hits(Validate(model, withDependency: false)));
    }
}
