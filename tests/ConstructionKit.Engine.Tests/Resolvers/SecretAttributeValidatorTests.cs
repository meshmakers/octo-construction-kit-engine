using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#5531 / AB#5528: compile-time rules of the Secret value type. Message numbers:
///     69 OwnerAttributeInvalid, 70 defaultValues, 71 assignment, 72 ownership, 73 index,
///     74 display rule, 75 System gate, 76 record key missing, 77 record key invalid.
/// </summary>
public class SecretAttributeValidatorTests
{
    private const string Model = "secretTest";

    private static CkCompiledModelRoot CreateModel()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(Model, "1.0.0"),
            Dependencies = [new("System", "2.5.0")],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Password", ValueType = AttributeValueTypesDto.Secret },
                new() { AttributeId = "Count", ValueType = AttributeValueTypesDto.Int },
                new()
                {
                    AttributeId = "Credentials", ValueType = AttributeValueTypesDto.RecordArray,
                    ValueCkRecordId = $"{Model}/Credential"
                },
                new()
                {
                    AttributeId = "Plain", ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = $"{Model}/PlainRecord"
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Configuration",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{Model}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{Model}/Password", AttributeName = "Password", IsOptional = true },
                        new() { CkAttributeId = $"{Model}/Credentials", AttributeName = "Credentials", IsOptional = true }
                    ]
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Credential",
                    RecordKey = "Name",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{Model}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{Model}/Password", AttributeName = "Value" }
                    ]
                },
                new()
                {
                    RecordId = "PlainRecord",
                    Attributes = [new() { CkAttributeId = $"{Model}/Name", AttributeName = "Name" }]
                }
            ]
        };
    }

    private static (CkModelGraph Graph, OperationResult Result) Validate(CkCompiledModelRoot model,
        List<CkModelIdVersionRange>? dependencyRanges = null)
    {
        var graph = new CkModelGraph();
        graph.AppendModel(sampleData.systemFake.Builder.Build());
        graph.AppendModel(model);

        var result = new OperationResult();
        var resolver = new InheritanceResolver(NullLogger<InheritanceResolver>.Instance);
        resolver.Resolve(graph, new OriginFileResolver("TEST"), result);

        SecretAttributeValidator.Validate(model, dependencyRanges ?? [new("System-[2.5,3.0)")], graph,
            new OriginFileResolver("TEST"), result);
        return (graph, result);
    }

    private static CkTypeAttributeDto TypeAttribute(CkCompiledModelRoot model, string name)
    {
        return model.Types!.Single().Attributes!.Single(a => a.AttributeName == name);
    }

    [Fact]
    public void ValidModel_NoMessages_AndOwnershipBecomesSecret()
    {
        var model = CreateModel();

        var (graph, result) = Validate(model);

        Assert.Empty(result.Messages);
        // Unset ownership becomes Secret - in the compiled DTO and in the graph.
        Assert.Equal(AttributeOwnershipDto.Secret, model.Attributes!.Single(a => a.AttributeId.Name == "Password").Ownership);
        var typeGraph = graph.Types[$"{Model}/Configuration"];
        Assert.Equal(AttributeOwnershipDto.Secret, typeGraph.AllAttributesByName["Password"].Ownership);
        Assert.True(typeGraph.AllAttributesByName["Password"].Ownership.IsExcludedFromExport());
    }

    [Fact]
    public void GraphOwnership_IsCoercedToSecret_EvenWhenDeclaredOtherwise()
    {
        var attributeGraph = new CkAttributeGraph($"{Model}/Password",
            new CkAttributeDto
            {
                AttributeId = "Password", ValueType = AttributeValueTypesDto.Secret,
                Ownership = AttributeOwnershipDto.SeedOwned
            });

        Assert.Equal(AttributeOwnershipDto.Secret, attributeGraph.Ownership);
        attributeGraph.Ownership = AttributeOwnershipDto.TenantOwned;
        Assert.Equal(AttributeOwnershipDto.Secret, attributeGraph.Ownership);
    }

    [Fact]
    public void DefaultValues_AreRejected()
    {
        var model = CreateModel();
        model.Attributes!.Single(a => a.AttributeId.Name == "Password").DefaultValues = ["changeme"];

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(70, message.MessageNumber);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.DoesNotContain("changeme", message.MessageText);
    }

    [Fact]
    public void AutoCompleteValues_AreRejected()
    {
        var model = CreateModel();
        TypeAttribute(model, "Password").AutoCompleteValues = ["a", "b"];

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(71, message.MessageNumber);
        Assert.Contains("autoCompleteValues", message.MessageText);
    }

    [Fact]
    public void AutoIncrementReference_IsRejected()
    {
        var model = CreateModel();
        TypeAttribute(model, "Password").AutoIncrementReference = "Counter";

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(71, message.MessageNumber);
        Assert.Contains("autoIncrementReference", message.MessageText);
    }

    [Fact]
    public void AssociationRoleAttribute_IsRejected()
    {
        var model = CreateModel();
        model.AssociationRoles =
        [
            new()
            {
                AssociationRoleId = "Uses", InboundName = "UsedBy", OutboundName = "Uses",
                InboundMultiplicity = MultiplicitiesDto.N, OutboundMultiplicity = MultiplicitiesDto.N,
                Attributes = [new() { CkAttributeId = $"{Model}/Password", AttributeName = "Password" }]
            }
        ];

        var (_, result) = Validate(model);

        Assert.Contains(result.Messages, m => m.MessageNumber == 71 && m.MessageText.Contains("association role"));
    }

    [Theory]
    [InlineData(AttributeOwnershipDto.SeedOwned)]
    [InlineData(AttributeOwnershipDto.TenantOwned)]
    [InlineData(AttributeOwnershipDto.RuntimeState)]
    public void OwnershipOverrideOnDefinition_IsRejected(AttributeOwnershipDto ownership)
    {
        var model = CreateModel();
        model.Attributes!.Single(a => a.AttributeId.Name == "Password").Ownership = ownership;

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(72, message.MessageNumber);
        Assert.Contains(ownership.ToString(), message.MessageText);
    }

    [Fact]
    public void OwnershipOverrideOnAssignment_IsRejected()
    {
        var model = CreateModel();
        TypeAttribute(model, "Password").Ownership = AttributeOwnershipDto.SeedOwned;

        var (graph, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(72, message.MessageNumber);
        // The graph still never treats the value as seed-owned.
        Assert.Equal(AttributeOwnershipDto.Secret,
            graph.Types[$"{Model}/Configuration"].AllAttributesByName["Password"].Ownership);
    }

    [Fact]
    public void ExplicitSecretOwnership_IsAccepted()
    {
        var model = CreateModel();
        model.Attributes!.Single(a => a.AttributeId.Name == "Password").Ownership = AttributeOwnershipDto.Secret;
        TypeAttribute(model, "Password").Ownership = AttributeOwnershipDto.Secret;

        var (_, result) = Validate(model);

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Index_OnSecret_IsRejected()
    {
        var model = CreateModel();
        model.Types!.Single().Indexes =
        [
            new() { IndexType = IndexTypeDto.Ascending, Fields = [new() { AttributePaths = ["Name", "Password"] }] }
        ];

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(73, message.MessageNumber);
        Assert.Contains("Password", message.MessageText);
    }

    [Fact]
    public void DisplayRule_ReferencingSecret_IsRejected()
    {
        var model = CreateModel();
        model.Types!.Single().DisplayNameRule = "${Name} (${Password})";

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(74, message.MessageNumber);
        Assert.Contains("displayNameRule", message.MessageText);
    }

    [Fact]
    public void OwnerAttribute_OnSecret_IsRejected()
    {
        var model = CreateModel();
        model.Types!.Single().OwnerAttributePath = "Password";

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(69, message.MessageNumber);
        Assert.Contains("Secret", message.MessageText);
    }

    [Theory]
    [InlineData("System-[2.4,3.0)")]
    [InlineData("System-[1.0,)")]
    public void SystemDependencyBelow25_IsRejected(string dependency)
    {
        var model = CreateModel();

        var (_, result) = Validate(model, [new(dependency)]);

        var message = Assert.Single(result.Messages);
        Assert.Equal(75, message.MessageNumber);
        Assert.Contains("2.5.0", message.MessageText);
    }

    [Fact]
    public void MissingSystemDependency_IsRejected()
    {
        var model = CreateModel();

        var (_, result) = Validate(model, [new("Basic-[2.0,3.0)")]);

        var message = Assert.Single(result.Messages);
        Assert.Equal(75, message.MessageNumber);
        Assert.Contains("no direct System dependency", message.MessageText);
    }

    [Fact]
    public void ModelWithoutSecret_NeedsNoSystem25()
    {
        var model = CreateModel();
        model.Attributes!.Single(a => a.AttributeId.Name == "Password").ValueType = AttributeValueTypesDto.String;

        var (_, result) = Validate(model, [new("System-[1.0,3.0)")]);

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void RecordWithSecret_WithoutRecordKey_IsRejected()
    {
        var model = CreateModel();
        model.Records!.Single(r => r.RecordId.Name == "Credential").RecordKey = null;

        var (_, result) = Validate(model);

        var message = Assert.Single(result.Messages);
        Assert.Equal(76, message.MessageNumber);
        Assert.Contains("'Value'", message.MessageText);
    }

    [Fact]
    public void RecordKey_IsInheritedByDerivedRecord()
    {
        var model = CreateModel();
        model.Records!.Add(new CkRecordDto
        {
            RecordId = "SpecialCredential",
            DerivedFromCkRecordId = $"{Model}/Credential",
            Attributes = [new() { CkAttributeId = $"{Model}/Count", AttributeName = "Count" }]
        });

        var (graph, result) = Validate(model);

        Assert.Empty(result.Messages);
        Assert.Equal("Name", graph.Records[$"{Model}/SpecialCredential"].RecordKey);
    }

    [Theory]
    [InlineData("Missing", "no attribute")]
    [InlineData("Value", "Secret attribute cannot be the record key")]
    public void RecordKey_Invalid_IsRejected(string recordKey, string reason)
    {
        var model = CreateModel();
        model.Records!.Single(r => r.RecordId.Name == "Credential").RecordKey = recordKey;

        var (_, result) = Validate(model);

        Assert.Contains(result.Messages, m => m.MessageNumber == 77 && m.MessageText.Contains(reason));
    }

    [Fact]
    public void RecordKey_Optional_IsRejected()
    {
        var model = CreateModel();
        model.Records!.Single(r => r.RecordId.Name == "Credential").Attributes!
            .Single(a => a.AttributeName == "Name").IsOptional = true;

        var (_, result) = Validate(model);

        Assert.Contains(result.Messages, m => m.MessageNumber == 77 && m.MessageText.Contains("optional"));
    }

    [Fact]
    public void QueryColumns_ExcludeSecretAttributes()
    {
        var model = CreateModel();
        var (graph, result) = Validate(model);
        Assert.Empty(result.Messages);

        var columns = graph.GetCkTypeQueryColumnPaths($"{Model}/Configuration",
            new CkTypeQueryColumnOptions { IgnoreNavigationProperties = true });

        Assert.Contains(columns, c => c.Path == "name");
        Assert.DoesNotContain(columns, c => c.ValueType == AttributeValueTypesDto.Secret);
        Assert.DoesNotContain(columns, c => c.Path.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Path.EndsWith(".value", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(columns, c => c.Path == "credentials[0].name");
    }
}
