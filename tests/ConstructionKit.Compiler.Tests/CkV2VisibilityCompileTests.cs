using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     F1.2-S3 (AB#5912): <c>visibility: Internal</c> elements cannot be referenced from another model (112) and
///     <c>derivable: Model</c> types/records cannot be derived from in another model (113) — at compile time and when
///     a compiled model is resolved (import), so a forged base model cannot bypass it.
/// </summary>
public sealed class CkV2VisibilityCompileTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static Dictionary<string, string> LibFiles() => new()
    {
        ["attributes/a.yaml"] = """
            attributes:
              - id: Pub
                valueType: String
              - id: Priv
                valueType: String
                visibility: Internal
              - id: Shade
                valueType: Enum
                valueCkEnumId: ${this}/Color
              - id: Where
                valueType: Record
                valueCkRecordId: ${this}/Address
            """,
        ["enums/e.yaml"] = """
            enums:
              - enumId: Color
                visibility: Internal
                values:
                  - { key: 0, name: Red }
            """,
        ["records/r.yaml"] = """
            records:
              - recordId: Address
                visibility: Internal
                attributes:
                  - id: ${this}/Pub
                    name: Pub
                    isOptional: true
              - recordId: OpenRecord
                derivable: Any
                attributes:
                  - id: ${this}/Pub
                    name: Pub
                    isOptional: true
              - recordId: ClosedRecord
                attributes:
                  - id: ${this}/Pub
                    name: Pub
                    isOptional: true
            """,
        ["associations/r.yaml"] = """
            associationRoles:
              - id: Link
                inboundName: LinkedFrom
                outboundName: LinksTo
                inboundMultiplicity: N
                outboundMultiplicity: N
                visibility: Internal
            """,
        ["interfaces/i.yaml"] = """
            interfaces:
              - interfaceId: Secretive-1
                visibility: Internal
                attributes:
                  - id: ${this}/Pub
                    name: Pub
                    isOptional: true
            """,
        ["types/t.yaml"] = """
            types:
              - typeId: Open
                derivedFromCkTypeId: ${System}/Entity
                derivable: Any
                attributes:
                  - id: ${this}/Shade
                    name: Shade
                    isOptional: true
                  - id: ${this}/Where
                    name: Where
                    isOptional: true
                associations:
                  - id: ${this}/Link
                    targetCkTypeId: ${this}/Open
              - typeId: Closed
                derivedFromCkTypeId: ${this}/Open
              - typeId: Secret
                derivedFromCkTypeId: ${this}/Open
                visibility: Internal
                derivable: Any
                implements:
                  - ${this}/Secretive-1
            """
    };

    private async Task<CkCompiledModelRoot> PublishLibAsync(Dictionary<string, string>? files = null)
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));
        // Inside its own model every internal element and every derivation is allowed.
        return await _fixture.CompileAndPublishAsync(_fixture.WriteSource($"lib-{Guid.NewGuid():N}", "Lib-1.0.0",
            ["System-[2.5,3.0)"], files ?? LibFiles(), ckLanguage: 2));
    }

    private async Task<OperationResult> CompileDependentAsync(string files, int? ckLanguage = null)
    {
        var operationResult = new OperationResult();
        var source = _fixture.WriteSource($"dep-{Guid.NewGuid():N}", "Dependent-1.0.0",
            ["System-[2.5,3.0)", "Lib-[1.0,2.0)"], new Dictionary<string, string> { ["types/t.yaml"] = files },
            ckLanguage);
        try
        {
            await _fixture.Services.GetRequiredService<Contracts.Services.ICompilerService>()
                .CompileInMemoryAsync(source, operationResult);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        return operationResult;
    }

    private static string Type(string body) =>
        "types:\n  - typeId: Mine\n" + body;

    [Theory]
    [InlineData("    derivedFromCkTypeId: ${Lib}/Secret\n", "type")]
    [InlineData("    derivedFromCkTypeId: ${Lib}/Open\n    attributes:\n      - id: ${Lib}/Priv\n        name: Priv\n        isOptional: true\n", "attribute")]
    [InlineData("    derivedFromCkTypeId: ${Lib}/Open\n    associations:\n      - id: ${Lib}/Link\n        targetCkTypeId: ${Lib}/Open\n", "association role")]
    [InlineData("    derivedFromCkTypeId: ${Lib}/Open\n    associations:\n      - id: ${Lib}/Link\n        targetCkTypeId: ${Lib}/Secret\n", "type")]
    public async Task ReferenceToAnInternalElement_Is112(string body, string kind)
    {
        await PublishLibAsync();

        var result = await CompileDependentAsync(Type(body));

        Assert.Contains(result.Messages, m => m.MessageNumber == 112 && m.MessageText.Contains($"internal {kind}"));
    }

    [Fact]
    public async Task ImplementingAnInternalInterface_Is112()
    {
        await PublishLibAsync();

        var result = await CompileDependentAsync(
            Type("    derivedFromCkTypeId: ${Lib}/Open\n    implements:\n      - ${Lib}/Secretive-1\n"), ckLanguage: 2);

        Assert.Contains(result.Messages, m => m.MessageNumber == 112 && m.MessageText.Contains("internal interface"));
    }

    [Fact]
    public async Task ReusingAnInternalEnumOrRecordThroughAnAttribute_Is112()
    {
        await PublishLibAsync();
        var operationResult = new OperationResult();
        var source = _fixture.WriteSource("dep-attr", "Dependent-1.0.0", ["System-[2.5,3.0)", "Lib-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] =
                    "attributes:\n  - id: Tint\n    valueType: Enum\n    valueCkEnumId: ${Lib}/Color\n" +
                    "  - id: Home\n    valueType: Record\n    valueCkRecordId: ${Lib}/Address\n"
            });
        try
        {
            await _fixture.Services.GetRequiredService<Contracts.Services.ICompilerService>()
                .CompileInMemoryAsync(source, operationResult);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 112 && m.MessageText.Contains("internal enum"));
        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 112 && m.MessageText.Contains("internal record"));
    }

    [Theory]
    [InlineData("${Lib}/Closed", 113)]
    [InlineData("${Lib}/Open", null)]
    public async Task DerivingAcrossModels_RespectsDerivable(string baseType, int? expected)
    {
        await PublishLibAsync();

        // A v1 dependent: the rule is about the base, not the dependent's language.
        var result = await CompileDependentAsync(Type($"    derivedFromCkTypeId: {baseType}\n"));

        if (expected == null)
        {
            Assert.DoesNotContain(result.Messages, m => m.MessageLevel >= Contracts.Messages.MessageLevel.Error);
        }
        else
        {
            Assert.Contains(result.Messages, m => m.MessageNumber == expected && m.MessageText.Contains("Closed"));
        }
    }

    [Fact]
    public async Task DerivingFromAClosedRecord_Is113()
    {
        await PublishLibAsync();
        var operationResult = new OperationResult();
        var source = _fixture.WriteSource("dep-rec", "Dependent-1.0.0", ["System-[2.5,3.0)", "Lib-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["records/r.yaml"] = "records:\n  - recordId: Mine\n    derivedFromCkRecordId: ${Lib}/ClosedRecord\n" +
                                     "  - recordId: Fine\n    derivedFromCkRecordId: ${Lib}/OpenRecord\n"
            });
        try
        {
            await _fixture.Services.GetRequiredService<Contracts.Services.ICompilerService>()
                .CompileInMemoryAsync(source, operationResult);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        var message = Assert.Single(operationResult.Messages, m => m.MessageNumber == 113);
        Assert.Contains("ClosedRecord", message.MessageText);
    }

    [Fact]
    public async Task ForgedBase_IsRefusedWhenTheDependentIsResolved()
    {
        // Compile the dependent against a base whose type is public, then replace the base (same version) with one
        // that makes the type internal — the import-time resolve must refuse the dependent.
        await PublishLibAsync();
        var operationResult = new OperationResult();
        var dependent = await _fixture.CompileAsync(_fixture.WriteSource("dep-forged", "Dependent-1.0.0",
            ["System-[2.5,3.0)", "Lib-[1.0,2.0)"],
            new Dictionary<string, string> { ["types/t.yaml"] = Type("    derivedFromCkTypeId: ${Lib}/Open\n") }));

        var forgedFiles = LibFiles();
        forgedFiles["types/t.yaml"] = forgedFiles["types/t.yaml"].Replace(
            "  - typeId: Open\n    derivedFromCkTypeId: ${System}/Entity\n    derivable: Any",
            "  - typeId: Open\n    derivedFromCkTypeId: ${System}/Entity\n    derivable: Any\n    visibility: Internal");
        var forged = await _fixture.CompileAsync(_fixture.WriteSource("lib-forged", "Lib-1.0.0", ["System-[2.5,3.0)"],
            forgedFiles, ckLanguage: 2));
        Assert.Equal(CkVisibilityDto.Internal, forged.Types!.Single(t => t.TypeId.Name == "Open").Visibility);
        await _fixture.Services.GetServices<ICatalog>().OfType<LocalFileSystemCatalog>().Single()
            .PublishAsync(forged, force: true);

        try
        {
            await _fixture.Services.GetRequiredService<ICatalogModelResolver>()
                .HardResolveAsync(dependent, new OriginFileResolver("-"), operationResult);
        }
        catch (Exception)
        {
            // a failed hard resolve may throw; asserted through the messages
        }

        Assert.True(operationResult.HasErrors);

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 112 && m.MessageText.Contains("Open"));
    }
}
