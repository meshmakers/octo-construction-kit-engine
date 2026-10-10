using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#4472: every range-retaining compiled model records the dependency surface it uses (usedSurface + hash).
/// </summary>
public sealed class UsedSurfaceCompileTests : IDisposable
{
    private readonly CkCompileFixture _on = new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = true));
    private readonly CkCompileFixture _off = new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = false));

    public void Dispose()
    {
        _on.Dispose();
        _off.Dispose();
    }

    /// <summary>A ckLanguage 2 System with every element kind a dependent can reference.</summary>
    private static async Task PublishSystemAsync(CkCompileFixture fixture)
    {
        await fixture.CompileAndPublishAsync(fixture.WriteSource("system-v2", "System-2.5.0", null,
            new Dictionary<string, string>
            {
                ["attributes/attributes.yaml"] = """
                    attributes:
                      - id: Name
                        valueType: String
                      - id: Description
                        valueType: String
                      - id: Street
                        valueType: String
                      - id: Unused
                        valueType: String
                    """,
                ["records/address.yaml"] = """
                    records:
                      - recordId: Address
                        derivable: Any
                        attributes:
                          - id: ${this}/Street
                            name: Street
                            isOptional: true
                    """,
                ["enums/mode.yaml"] = """
                    enums:
                      - enumId: Mode
                        values:
                          - key: 0
                            name: On
                    """,
                ["associations/roles.yaml"] = """
                    associationRoles:
                      - id: Owns
                        inboundName: OwnedBy
                        outboundName: Owns
                        inboundMultiplicity: N
                        outboundMultiplicity: N
                    """,
                ["interfaces/named.yaml"] = """
                    interfaces:
                      - interfaceId: Named-1
                        attributes:
                          - id: ${this}/Name
                            name: Name
                    """,
                ["types/entity.yaml"] = """
                    types:
                      - typeId: Entity
                        isAbstract: true
                        derivable: Any
                        attributes:
                          - id: ${this}/Name
                            name: Name
                    """
            }, ckLanguage: 2));
    }

    private static Dictionary<string, string> DependentFiles(bool permuted = false)
    {
        var files = new Dictionary<string, string>
        {
            ["attributes/own.yaml"] = """
                attributes:
                  - id: Mode
                    valueType: Enum
                    valueCkEnumId: ${System}/Mode
                  - id: Home
                    valueType: Record
                    valueCkRecordId: ${System}/Address
                  - id: Code
                    valueType: String
                """,
            ["types/thing.yaml"] = """
                types:
                  - typeId: Thing
                    derivedFromCkTypeId: ${System}/Entity
                    derivable: Any
                    implements:
                      - ${System}/Named-1
                    ownerAttributePath: Name
                    attributes:
                      - id: ${System}/Description
                        name: Description
                        isOptional: true
                      - id: ${this}/Mode
                        name: Mode
                        isOptional: true
                      - id: ${this}/Home
                        name: Home
                        isOptional: true
                      - id: ${this}/Code
                        name: Code
                        isOptional: true
                    associations:
                      - id: ${System}/Owns
                        targetCkTypeId: ${System}/Entity
                    indexes:
                      - indexType: Ascending
                        fields:
                          - attributePaths: [ name ]
                          - attributePaths: [ code ]
                """
        };

        return permuted ? files.Reverse().ToDictionary(p => p.Key, p => p.Value) : files;
    }

    private static readonly string[] ExpectedSurface =
    [
        "System@2/Address-1",
        "System@2/Description-1",
        "System@2/Entity-1",
        "System@2/Entity-1.Name",
        "System@2/Mode-1",
        "System@2/Named-1",
        "System@2/Owns-1"
    ];

    private async Task<CkModelDependencyDto> CompileDependentAsync(string name, bool permuted = false)
    {
        var compiled = await _on.CompileAsync(_on.WriteSource(name, "Dep-1.0.0", ["System-[2.5,3.0)"],
            DependentFiles(permuted), ckLanguage: 2));
        return Assert.Single(compiled.DependencyRanges!);
    }

    [Fact]
    public async Task EveryReferenceKind_IsListed_OwnReferencesAreNot()
    {
        await PublishSystemAsync(_on);

        var dependency = await CompileDependentAsync("dep");

        Assert.Equal(ExpectedSurface, dependency.UsedSurface);
        Assert.Equal(CkUsedSurfaceCollector.Hash(ExpectedSurface), dependency.UsedSurfaceHash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", dependency.UsedSurfaceHash);
        Assert.DoesNotContain(dependency.UsedSurface!, e => e.StartsWith("Dep", StringComparison.Ordinal));
        Assert.DoesNotContain("System@2/Unused-1", dependency.UsedSurface!);
    }

    [Fact]
    public async Task CompilingTwiceAndWithPermutedFileOrder_GivesTheSameListAndHash()
    {
        await PublishSystemAsync(_on);

        var first = await CompileDependentAsync("dep-a");
        var second = await CompileDependentAsync("dep-b");
        var permuted = await CompileDependentAsync("dep-c", permuted: true);

        Assert.Equal(first.UsedSurface, second.UsedSurface);
        Assert.Equal(first.UsedSurfaceHash, second.UsedSurfaceHash);
        Assert.Equal(first.UsedSurface, permuted.UsedSurface);
        Assert.Equal(first.UsedSurfaceHash, permuted.UsedSurfaceHash);
    }

    [Fact]
    public async Task WithoutRangeRetention_ThereIsNoUsedSurface()
    {
        await PublishSystemAsync(_off);

        var compiled = await _off.CompileAsync(_off.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"],
            DependentFiles(), ckLanguage: 2));

        Assert.Null(compiled.DependencyRanges);
        Assert.DoesNotContain("usedSurface", await ToYamlAsync(_off, compiled));
    }

    [Fact]
    public async Task UsedSurface_SurvivesYamlSchemaValidationAndTheLocalCatalogRoundTrip()
    {
        await PublishSystemAsync(_on);
        var compiled = await _on.CompileAsync(_on.WriteSource("dep", "Dep-1.0.0", ["System-[2.5,3.0)"],
            DependentFiles(), ckLanguage: 2));

        var yaml = await ToYamlAsync(_on, compiled);
        Assert.Contains("usedSurface:", yaml);
        Assert.Contains("usedSurfaceHash: sha256:", yaml);
        var operationResult = new OperationResult();
        var readBack = await _on.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(yaml, "dep.yaml", operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        Assert.Equal(ExpectedSurface, readBack.DependencyRanges!.Single().UsedSurface);

        // Publish to the local file-system catalog and reload.
        var catalogService = _on.Services.GetRequiredService<ICatalogService>();
        await catalogService.PublishAsync(LocalFileSystemCatalog.Name, compiled, new OriginFileResolver("-"), isForced: true);
        var reloaded = await catalogService.GetAsync(LocalFileSystemCatalog.Name, compiled.ModelId, new OperationResult());
        Assert.Equal(ExpectedSurface, reloaded!.DependencyRanges!.Single().UsedSurface);
        Assert.Equal(compiled.DependencyRanges!.Single().UsedSurfaceHash, reloaded.DependencyRanges!.Single().UsedSurfaceHash);
    }

    private static async Task<string> ToYamlAsync(CkCompileFixture fixture, CkCompiledModelRoot model)
    {
        await using var memoryStream = new MemoryStream();
        await using (var writer = new StreamWriter(memoryStream, new System.Text.UTF8Encoding(false), 4096, true))
        {
            await fixture.Services.GetRequiredService<ICkSerializer>().SerializeAsync(writer, model);
        }

        return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
    }
}
