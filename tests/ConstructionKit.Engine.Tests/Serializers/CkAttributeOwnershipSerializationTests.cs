using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Serializers;

/// <summary>
///     AB#5187 — the wire side of the ownership model. Two things have to hold, and both are
///     schema-level rather than code-level, which is why they get their own tests: the JSON schema
///     must accept <c>ownership</c> on the attribute definition AND on an assignment (the element
///     schemas set <c>additionalProperties: false</c>, so an unlisted key is a hard parse failure),
///     and a serialised compiled model must carry the deprecated <c>isRuntimeState</c> mirror
///     alongside the enum so an engine that pre-dates ownership degrades safely under version skew.
/// </summary>
public class CkAttributeOwnershipSerializationTests
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static async Task<CkElementsRootDto> DeserializeElementsAsync(string yaml)
    {
        var serializer = new CkYamlSerializer(new CkSchemaValidator());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(yaml));
        var operationResult = new OperationResult();
        var elements = await serializer.DeserializeElementsAsync(stream, "inline.yaml", operationResult);
        Assert.False(operationResult.HasErrors);
        return elements;
    }

    [Fact]
    public async Task AttributeDefinition_AcceptsEveryOwnershipValue()
    {
        var elements = await DeserializeElementsAsync(
            """
            "$schema": "https://schemas.meshmakers.cloud/construction-kit-elements.schema.json"
            attributes:
              - id: ReportName
                valueType: String
                ownership: SeedOwned
              - id: TaxRate
                valueType: Double
                ownership: TenantOwned
              - id: DeploymentState
                valueType: Int
                ownership: RuntimeState
              - id: ApiKey
                valueType: String
                ownership: Secret
            """);

        var byId = elements.Attributes!.ToDictionary(a => a.AttributeId.Name, a => a);
        Assert.Equal(AttributeOwnershipDto.SeedOwned, byId["ReportName"].Ownership);
        Assert.Equal(AttributeOwnershipDto.TenantOwned, byId["TaxRate"].Ownership);
        Assert.Equal(AttributeOwnershipDto.RuntimeState, byId["DeploymentState"].Ownership);
        Assert.Equal(AttributeOwnershipDto.Secret, byId["ApiKey"].Ownership);

        // The mirror: TenantOwned is preserved on Upsert too, which is what the alias now means.
        Assert.False(byId["ReportName"].IsRuntimeState);
        Assert.True(byId["TaxRate"].IsRuntimeState);
        Assert.True(byId["ApiKey"].IsRuntimeState);
    }

    [Fact]
    public async Task AttributeDefinition_LegacyBooleanStillParsesUnchanged()
    {
        var elements = await DeserializeElementsAsync(
            """
            "$schema": "https://schemas.meshmakers.cloud/construction-kit-elements.schema.json"
            attributes:
              - id: Designation
                valueType: String
              - id: ArchiveStatus
                valueType: Int
                isRuntimeState: true
              - id: PageSize
                valueType: Int
                isRuntimeState: false
            """);

        var byId = elements.Attributes!.ToDictionary(a => a.AttributeId.Name, a => a);
        Assert.Null(byId["Designation"].Ownership);
        Assert.False(byId["Designation"].IsRuntimeState);
        Assert.True(byId["ArchiveStatus"].IsRuntimeState);
        Assert.False(byId["PageSize"].IsRuntimeState);
    }

    [Fact]
    public async Task Assignment_AcceptsThePerAssignmentOverride_OnTypesAndRecords()
    {
        var elements = await DeserializeElementsAsync(
            """
            "$schema": "https://schemas.meshmakers.cloud/construction-kit-elements.schema.json"
            types:
              - typeId: ServiceAccountConfiguration
                attributes:
                  - id: ${this}/ClientId
                    name: ClientId
                    ownership: SeedOwned
                  - id: ${this}/ClientSecret
                    name: ClientSecret
            records:
              - recordId: UiThemeColors
                attributes:
                  - id: ${this}/PrimaryColor
                    name: PrimaryColor
                    ownership: TenantOwned
            """);

        var typeAssignments = elements.Types!.Single().Attributes!;
        Assert.Equal(AttributeOwnershipDto.SeedOwned,
            typeAssignments.Single(a => a.AttributeName == "ClientId").Ownership);
        Assert.Null(typeAssignments.Single(a => a.AttributeName == "ClientSecret").Ownership);

        Assert.Equal(AttributeOwnershipDto.TenantOwned,
            elements.Records!.Single().Attributes!.Single().Ownership);
    }

    [Fact]
    public async Task SerializedCompiledModel_CarriesTheEnumAndTheMirror()
    {
        var model = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(model, "StateAttr").Ownership = AttributeOwnershipDto.Secret;
        SemVerTestModels.GetAttribute(model, "WithDefault").Ownership = AttributeOwnershipDto.SeedOwned;

        var yaml = await SerializeAsync(model);

        // The enum is what a current engine reads; the mirror is what an older engine reads, and
        // it must say "preserved" for a Secret so version skew degrades instead of resetting it.
        Assert.Contains("ownership: Secret", yaml);
        Assert.Contains("isRuntimeState: true", yaml);
        // SeedOwned mirrors to false, which YamlDotNet omits as the default — no noise, and an
        // older engine reads exactly today's behaviour.
        Assert.DoesNotContain("isRuntimeState: false", yaml);
    }

    [Fact]
    public async Task SerializedCompiledModel_WithoutOwnership_IsUnchangedFromBefore()
    {
        // Back-compat guard: a model that takes no position must serialise without either key,
        // so recompiling with this engine produces a byte-identical attribute block.
        var yaml = await SerializeAsync(SemVerTestModels.CreateModel());

        Assert.DoesNotContain("ownership:", yaml);
        Assert.DoesNotContain("isRuntimeState:", yaml);
    }

    [Fact]
    public async Task JsonCompiledModel_RoundTripsOwnershipThroughSchemaValidation()
    {
        // The JSON lane is the one the model catalog and the CK-model repository use, and its
        // serializer validates against the same schema on the way in and out — a property the
        // schema does not list fails hard rather than silently dropping.
        var model = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(model, "StateAttr").Ownership = AttributeOwnershipDto.TenantOwned;
        model.Types!.Single().Attributes!.Single(a => a.AttributeName == "State").Ownership =
            AttributeOwnershipDto.Secret;

        var serializer = new CkJsonSerializer();
        using var stream = new MemoryStream();
        // No BOM: CkJsonSerializer writes straight to StreamWriter.BaseStream, so a preamble
        // emitted when the writer is flushed would land AFTER the JSON, not before it.
        await using (var writer = new StreamWriter(stream, NoBomUtf8, leaveOpen: true))
        {
            await serializer.SerializeAsync(writer, model);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"ownership\": \"TenantOwned\"", json);
        Assert.Contains("\"ownership\": \"Secret\"", json);

        var operationResult = new OperationResult();
        var roundTripped = serializer.DeserializeCompiledModelRoot(json, "inline.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Equal(AttributeOwnershipDto.TenantOwned,
            roundTripped.Attributes!.Single(a => a.AttributeId.Name == "StateAttr").Ownership);
        Assert.Equal(AttributeOwnershipDto.Secret,
            roundTripped.Types!.Single().Attributes!.Single(a => a.AttributeName == "State").Ownership);
    }

    private static async Task<string> SerializeAsync(CkCompiledModelRoot model)
    {
        var serializer = new CkYamlSerializer(new CkSchemaValidator());
        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, NoBomUtf8, leaveOpen: true);
        await serializer.SerializeAsync(writer, model);
        await writer.FlushAsync(TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
