using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     CK v2 Phase 0 end to end from YAML sources (AB#5667 / AB#5668 / AB#5669): the <c>interfaces/</c> folder,
///     variable expansion of <c>implements</c> and method references, the ckLanguage gate, the compiled output and
///     range retention of interface references (F0.2 composition).
/// </summary>
public sealed class CkV2CompileTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new(s => s.AddDocumentationService());

    private readonly CkCompileFixture _rangeRetention =
        new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = true));

    public void Dispose()
    {
        _fixture.Dispose();
        _rangeRetention.Dispose();
    }

    private static Dictionary<string, string> KitchenSinkFiles() => new()
    {
        ["attributes/attributes.yaml"] = """
            attributes:
              - id: Serial
                valueType: String
              - id: PasswordHash
                valueType: String
              - id: Street
                valueType: String
            """,
        ["records/address.yaml"] = """
            records:
              - recordId: Address
                attributes:
                  - id: ${this}/Street
                    name: Street
                    isOptional: true
                    access: Hidden
            """,
        ["enums/mode.yaml"] = """
            enums:
              - enumId: Mode
                values:
                  - key: 0
                    name: Change
                  - key: 1
                    name: Reset
            """,
        ["interfaces/named.yaml"] = """
            interfaces:
              - interfaceId: Named-1
                description: Anything with a name
                attributes:
                  - id: ${System}/Name
                    name: Name
              - interfaceId: Serialized-1
                attributes:
                  - id: ${this}/Serial
                    name: Serial
            """,
        ["types/types.yaml"] = """
            types:
              - typeId: Principal
                isAbstract: true
                derivedFromCkTypeId: ${System}/Entity
                implements:
                  - ${this}/Named-1
                attributes:
                  - id: ${System}/Name
                    name: Name
                    access: ReadOnly
                methods:
                  - methodId: ChangePassword-1
                    parameters:
                      - name: newPassword
                        valueType: String
                        sensitive: true
                      - name: mode
                        valueType: Enum
                        valueCkEnumId: ${this}/Mode
                    result:
                      valueType: Record
                      valueCkRecordId: ${this}/Address
                    errors:
                      - code: PASSWORD_POLICY_VIOLATION
                    authorization:
                      roles: [ UserManagement ]
                      allowSelf: true
                    execution:
                      timeoutSeconds: 15
              - typeId: Account
                derivedFromCkTypeId: ${this}/Principal
                implements:
                  - ${this}/Serialized-1
                attributes:
                  - id: ${this}/Serial
                    name: Serial
                  - id: ${this}/PasswordHash
                    name: PasswordHash
                    isOptional: true
                    access: Hidden
                methods:
                  - methodId: Unlock-1
            """
    };

    private async Task PublishSystemAsync(CkCompileFixture fixture)
    {
        // The fixture System assigns Name as optional on Entity; the Principal re-assignment would collide, so
        // the interface member Name is assigned on Principal from a System without that assignment.
        await fixture.CompileAndPublishAsync(fixture.WriteSource("system", "System-2.5.0", null,
            new Dictionary<string, string>
            {
                ["attributes/attributes.yaml"] = "attributes:\n  - id: Name\n    valueType: String\n",
                ["types/entity.yaml"] = "types:\n  - typeId: Entity\n    isAbstract: true\n"
            }));
    }

    [Fact]
    public async Task Compile_ckv2_kitchen_sink_ok()
    {
        await PublishSystemAsync(_fixture);

        var compiled = await _fixture.CompileAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));

        Assert.Equal(2, compiled.CkLanguage);
        Assert.Equal(["Named-1", "Serialized-1"], compiled.Interfaces!.Select(i => i.InterfaceId.FullName));
        Assert.Equal("System-2.5.0/Name-1",
            compiled.Interfaces![0].Attributes.Single().CkAttributeId.FullName);
        var principal = compiled.Types!.Single(t => t.TypeId.Name == "Principal");
        Assert.Equal("KitchenSink-1.0.0/Named-1", principal.Implements!.Single().FullName);
        var method = principal.Methods!.Single();
        Assert.Equal("KitchenSink-1.0.0/Mode-1", method.Parameters![1].ValueCkEnumId!.FullName);
        Assert.Equal("KitchenSink-1.0.0/Address-1", method.Result!.ValueCkRecordId!.FullName);
        Assert.Equal(CkAttributeAccessDto.ReadOnly, principal.Attributes!.Single().Access);
        Assert.Equal(CkAttributeAccessDto.Hidden, compiled.Records!.Single().Attributes!.Single().Access);

        // The compiled YAML validates against the compiled schema and reads back.
        var yaml = await ToYamlAsync(_fixture, compiled);
        var operationResult = new OperationResult();
        var readBack = await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(yaml, "ks.yaml", operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        Assert.Equal(2, readBack.CkLanguage);
        Assert.Equal(2, readBack.Interfaces!.Count);
        Assert.Equal("Unlock-1", readBack.Types!.Single(t => t.TypeId.Name == "Account").Methods!.Single().MethodId);
    }

    [Fact]
    public async Task Compile_ckv2_kitchen_sink_GraphHasInheritedInterfacesAndMethods()
    {
        await PublishSystemAsync(_fixture);
        var compiled = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));

        var graph = await _fixture.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(compiled, new OriginFileResolver("-"), new OperationResult());

        var account = graph.Types[new CkId<CkTypeId>("KitchenSink-1.0.0/Account")];
        Assert.Equal(["Named-1", "Serialized-1"],
            account.AllImplementedInterfaces.Select(i => i.ElementId.FullName).OrderBy(n => n));
        Assert.Equal(["ChangePassword-1", "Unlock-1"], account.AllMethods.Keys.OrderBy(k => k));
        Assert.Equal("KitchenSink/Principal.ChangePassword-1", account.AllMethods["ChangePassword-1"].QualifiedMethodId);
        Assert.Equal(CkAttributeAccessDto.Hidden, account.AllAttributesByName["PasswordHash"].Access);
        Assert.Equal(["Account", "Principal"],
            graph.Interfaces[new CkId<CkInterfaceId>("KitchenSink-1.0.0/Named-1")].ImplementingTypes
                .Select(t => t.ElementId.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Compile_ckv2_keys_without_ckLanguage2_fails_with_90()
    {
        await PublishSystemAsync(_fixture);

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0", ["System-[2.5,3.0)"],
                KitchenSinkFiles()), operationResult));

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 90);
        Assert.All(operationResult.Messages.Where(m => m.MessageLevel >= Contracts.Messages.MessageLevel.Error),
            m => Assert.Equal(90, m.MessageNumber));
    }

    [Fact]
    public async Task RangeRetention_MajorQualifiesForeignInterfaceReferences_AndResolvesLater()
    {
        await PublishSystemAsync(_rangeRetention);
        await _rangeRetention.CompileAndPublishAsync(_rangeRetention.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));

        var consumer = await _rangeRetention.CompileAndPublishAsync(_rangeRetention.WriteSource("consumer",
            "Consumer-1.0.0", ["System-[2.5,3.0)", "KitchenSink-[1.0,2.0)"], new Dictionary<string, string>
            {
                ["types/badge.yaml"] = """
                    types:
                      - typeId: Badge
                        derivedFromCkTypeId: ${System}/Entity
                        implements:
                          - ${KitchenSink}/Named-1
                        attributes:
                          - id: ${System}/Name
                            name: Name
                    """
            }, ckLanguage: 2));

        Assert.Equal("KitchenSink@1/Named-1", consumer.Types!.Single().Implements!.Single().FullName);

        var graph = await _rangeRetention.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(consumer, new OriginFileResolver("-"), new OperationResult());
        var badge = graph.Types[new CkId<CkTypeId>("Consumer-1.0.0/Badge")];
        Assert.Equal("KitchenSink-1.0.0/Named-1", badge.AllImplementedInterfaces.Single().FullName);
    }

    [Fact]
    public async Task Docs_ListInterfacesImplementsAndMethods()
    {
        await PublishSystemAsync(_fixture);
        var compiled = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));
        var graph = await _fixture.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(compiled, new OriginFileResolver("-"), new OperationResult());
        var docs = Path.Combine(_fixture.Root, "docs");
        var generator = _fixture.Services.GetRequiredService<Engine.Documentation.IContentGenerator>();

        await generator.GenerateInterfacesMarkdownTable(graph, docs, compiled.ModelId, "1.0.0");
        await generator.GenerateTypesMarkdownTable(graph, docs, compiled.ModelId, "1.0.0", "/docs/");

        var files = Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories);
        var interfaces = await File.ReadAllTextAsync(files.Single(f => f.EndsWith("Interfaces.md")),
            TestContext.Current.CancellationToken);
        Assert.Contains("Named-1", interfaces);
        Assert.Contains("| Name | False | String | System/Name-1 |", interfaces);
        Assert.True(interfaces.Contains("Implemented by: `KitchenSink/Account`, `KitchenSink/Principal`"), interfaces);
        var types = await File.ReadAllTextAsync(files.Single(f => f.EndsWith("Types.md")),
            TestContext.Current.CancellationToken);
        Assert.Contains("Implements: `KitchenSink/Named-1`, `KitchenSink/Serialized-1`", types);
        Assert.Contains("| ChangePassword-1 | Instance | newPassword: String, mode: Enum | Record | KitchenSink/Principal |", types);
    }

    private static Dictionary<string, string> KitchenSinkWithModifiers()
    {
        // F1.1-S4 (AB#5907): visibility / derivable on every element kind that carries them.
        var files = KitchenSinkFiles();
        files["attributes/attributes.yaml"] = files["attributes/attributes.yaml"]
            .Replace("  - id: PasswordHash\n    valueType: String", "  - id: PasswordHash\n    valueType: String\n    visibility: Internal");
        files["records/address.yaml"] = files["records/address.yaml"]
            .Replace("  - recordId: Address", "  - recordId: Address\n    derivable: Any");
        files["enums/mode.yaml"] = files["enums/mode.yaml"]
            .Replace("  - enumId: Mode", "  - enumId: Mode\n    visibility: Internal");
        files["interfaces/named.yaml"] = files["interfaces/named.yaml"]
            .Replace("  - interfaceId: Serialized-1", "  - interfaceId: Serialized-1\n    visibility: Internal");
        files["types/types.yaml"] = files["types/types.yaml"]
            .Replace("  - typeId: Principal", "  - typeId: Principal\n    derivable: Any")
            .Replace("  - typeId: Account", "  - typeId: Account\n    visibility: Internal")
            .Replace("      - methodId: Unlock-1", "      - methodId: Unlock-1\n        visibility: Internal");
        files["associations/roles.yaml"] = """
            associationRoles:
              - id: Owns
                inboundName: OwnedBy
                outboundName: Owns
                inboundMultiplicity: N
                outboundMultiplicity: N
                visibility: Internal
            """;
        return files;
    }

    [Fact]
    public async Task Compile_ckv2_modifiers_reach_compiled_output_graph_and_docs()
    {
        await PublishSystemAsync(_fixture);
        var compiled = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkWithModifiers(), ckLanguage: 2));

        Assert.Equal(CkDerivableDto.Any, compiled.Types!.Single(t => t.TypeId.Name == "Principal").Derivable);
        var account = compiled.Types!.Single(t => t.TypeId.Name == "Account");
        Assert.Equal(CkVisibilityDto.Internal, account.Visibility);
        Assert.Null(account.Derivable);
        Assert.Equal(CkVisibilityDto.Internal, account.Methods!.Single().Visibility);
        Assert.Equal(CkVisibilityDto.Internal, compiled.Attributes!.Single(a => a.AttributeId.Name == "PasswordHash").Visibility);
        Assert.Equal(CkDerivableDto.Any, compiled.Records!.Single().Derivable);
        Assert.Equal(CkVisibilityDto.Internal, compiled.Enums!.Single().Visibility);
        Assert.Equal(CkVisibilityDto.Internal, compiled.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1").Visibility);
        Assert.Equal(CkVisibilityDto.Internal, compiled.AssociationRoles!.Single().Visibility);

        var graph = await _fixture.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(compiled, new OriginFileResolver("-"), new OperationResult());
        Assert.Equal(CkDerivableDto.Model, graph.Types["KitchenSink/Account"].Derivable);
        Assert.Equal(CkDerivableDto.Any, graph.Types["KitchenSink/Principal"].Derivable);

        var docs = Path.Combine(_fixture.Root, "docs-modifiers");
        var generator = _fixture.Services.GetRequiredService<Engine.Documentation.IContentGenerator>();
        await generator.GenerateTypesMarkdownTable(graph, docs, compiled.ModelId, "1.0.0", "/docs/");
        var types = await File.ReadAllTextAsync(
            Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories).Single(f => f.EndsWith("Types.md")),
            TestContext.Current.CancellationToken);
        Assert.Contains("Visibility: `Internal`", types);
        Assert.Contains("Derivable: `Model`", types);
        Assert.Contains("| Unlock-1 (internal) |", types);
    }

    [Fact]
    public async Task Compile_ckv2_interface_completion_reaches_graph_and_docs()
    {
        // F1.1-S5 (AB#5908)
        await PublishSystemAsync(_fixture);
        var files = KitchenSinkFiles();
        files["associations/roles.yaml"] = """
            associationRoles:
              - id: Owns
                inboundName: OwnedBy
                outboundName: Owns
                inboundMultiplicity: N
                outboundMultiplicity: N
            """;
        files["interfaces/labeled.yaml"] = """
            interfaces:
              - interfaceId: Labeled-1
                deprecated: true
                extends:
                  - ${this}/Named-1
                associations:
                  - id: ${this}/Owns
                    targetCkInterfaceId: ${this}/Serialized-1
                    isOptional: true
                methods:
                  - methodId: Relabel-1
                    parameters:
                      - name: mode
                        valueType: Enum
                        valueCkEnumId: ${this}/Mode
            """;
        files["types/types.yaml"] = files["types/types.yaml"].Replace("""
                implements:
                  - ${this}/Serialized-1
            """, """
                implements:
                  - ${this}/Serialized-1
                  - ${this}/Labeled-1
                associations:
                  - id: ${this}/Owns
                    targetCkTypeId: ${System}/Entity
                    targetCkInterfaceId: ${this}/Serialized-1
            """);
        var compiled = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], files, ckLanguage: 2));

        var labeled = compiled.Interfaces!.Single(i => i.InterfaceId.FullName == "Labeled-1");
        Assert.Equal("KitchenSink-1.0.0/Named-1", labeled.Extends!.Single().FullName);
        Assert.Equal("KitchenSink-1.0.0/Mode-1", labeled.Methods!.Single().Parameters![0].ValueCkEnumId!.FullName);
        Assert.Equal("KitchenSink-1.0.0/Serialized-1",
            compiled.Types!.Single(t => t.TypeId.Name == "Account").Associations!.Single().TargetCkInterfaceId!.FullName);

        var graph = await _fixture.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(compiled, new OriginFileResolver("-"), new OperationResult());
        Assert.Contains(graph.Types["KitchenSink/Account"].AllImplementedInterfaces, i => i.ElementId.FullName == "Named-1");
        Assert.Equal(["Name"], graph.Interfaces["KitchenSink/Labeled-1"].AllAttributes.Values.Select(a => a.AttributeName));

        var docs = Path.Combine(_fixture.Root, "docs-interfaces");
        var generator = _fixture.Services.GetRequiredService<Engine.Documentation.IContentGenerator>();
        await generator.GenerateInterfacesMarkdownTable(graph, docs, compiled.ModelId, "1.0.0");
        var interfaces = await File.ReadAllTextAsync(
            Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories).Single(f => f.EndsWith("Interfaces.md")),
            TestContext.Current.CancellationToken);
        Assert.Contains("**Deprecated.**", interfaces);
        Assert.Contains("Extends: `KitchenSink/Named-1`", interfaces);
        Assert.Contains("| KitchenSink/Owns-1 | KitchenSink/Serialized-1 | any | True | KitchenSink/Labeled-1 |", interfaces);
        Assert.Contains("| Relabel-1 | Instance | mode: Enum | - | KitchenSink/Labeled-1 |", interfaces);
    }

    [Fact]
    public async Task Compile_ckv2_sets_minEngineVersion_and_publishes_under_v3()
    {
        // F1.1-S6 (AB#5909)
        await PublishSystemAsync(_fixture);
        var compiled = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));

        Assert.Equal(Engine.Versioning.CkEngineVersion.CkV2MinEngineVersion, compiled.MinEngineVersion);
        Assert.True(File.Exists(Path.Combine(_fixture.CatalogDir, "ck-models/v3/k/KitchenSink/1/ck-kitchensink-1.0.0.json")));
        Assert.True(File.Exists(Path.Combine(_fixture.CatalogDir, "ck-models/v2/s/System/2/ck-system-2.5.0.json")));
        Assert.False(Directory.Exists(Path.Combine(_fixture.CatalogDir, "ck-models/v2/k")));
        Assert.Contains("minEngineVersion: 3.4.0", await ToYamlAsync(_fixture, compiled));
    }

    [Fact]
    public async Task V1_model_on_a_v2_dependency_inherits_minEngineVersion_and_goes_to_v3()
    {
        // Review G3 E-M4: an engine that cannot read the dependency must not see the dependent either.
        await PublishSystemAsync(_fixture);
        await _fixture.CompileAndPublishAsync(_fixture.WriteSource("ks", "KitchenSink-1.0.0",
            ["System-[2.5,3.0)"], KitchenSinkFiles(), ckLanguage: 2));
        var consumer = await _fixture.CompileAndPublishAsync(_fixture.WriteSource("v1consumer", "Consumer-1.0.0",
            ["System-[2.5,3.0)", "KitchenSink-[1.0,2.0)"], new Dictionary<string, string>
            {
                ["types/t.yaml"] = "types:\n  - typeId: Gadget\n    derivedFromCkTypeId: ${System}/Entity\n" +
                                   "    attributes:\n      - id: ${KitchenSink}/Serial\n        name: Serial\n        isOptional: true\n"
            }));

        Assert.Null(consumer.CkLanguage);
        Assert.Equal(Engine.Versioning.CkEngineVersion.CkV2MinEngineVersion, consumer.MinEngineVersion);
        Assert.True(File.Exists(Path.Combine(_fixture.CatalogDir, "ck-models/v3/c/Consumer/1/ck-consumer-1.0.0.json")));
        Assert.False(Directory.Exists(Path.Combine(_fixture.CatalogDir, "ck-models/v2/c")));
    }

    [Fact]
    public async Task Dependency_requiring_a_newer_engine_fails_with_126()
    {
        // Pin the running engine: DebugL is 999.0.0, CI 0.1.* (check skipped), release builds 3.x (AB#6274).
        using var _ = CkEngineVersion.OverrideCurrentForTests(new Version(3, 4, 149));
        await PublishSystemAsync(_fixture);
        var system = await _fixture.CompileAsync(_fixture.WriteSource("system26", "System-2.6.0", null,
            new Dictionary<string, string>
            {
                ["attributes/attributes.yaml"] = "attributes:\n  - id: Name\n    valueType: String\n",
                ["types/entity.yaml"] = "types:\n  - typeId: Entity\n    isAbstract: true\n"
            }));
        system.MinEngineVersion = "1000.0.0";
        // The catalog service resolves before it publishes and would refuse the model (126); write it directly.
        await _fixture.Services.GetServices<ICatalog>().OfType<LocalFileSystemCatalog>().Single()
            .PublishAsync(system, force: true);

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(_fixture.WriteSource("dependent", "Dependent-1.0.0", ["System-[2.6,3.0)"],
                new Dictionary<string, string>
                {
                    ["types/thing.yaml"] = "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n"
                }), operationResult));

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 126 && m.MessageText.Contains("1000.0.0"));
    }

    [Fact]
    public async Task Elements_in_the_wrong_folder_warn_with_110()
    {
        await PublishSystemAsync(_fixture);
        var files = KitchenSinkFiles();
        files["types/types.yaml"] += "\ninterfaces:\n  - interfaceId: Stray-1\n    attributes:\n      - id: ${System}/Name\n        name: Name\n";
        var operationResult = new OperationResult();

        var compiled = await _fixture.Services.GetRequiredService<ICompilerService>().CompileInMemoryAsync(
            _fixture.WriteSource("ks-stray", "KitchenSink-1.0.0", ["System-[2.5,3.0)"], files, ckLanguage: 2),
            operationResult);

        var warning = Assert.Single(operationResult.Messages, m => m.MessageNumber == 110);
        Assert.Equal(Contracts.Messages.MessageLevel.Warning, warning.MessageLevel);
        Assert.Contains("'interfaces'", warning.MessageText);
        Assert.Contains("'interfaces/'", warning.MessageText);
        Assert.DoesNotContain(compiled.Interfaces!, i => i.InterfaceId.FullName == "Stray-1");
    }

    private static async Task<string> ToYamlAsync(CkCompileFixture fixture, CkCompiledModelRoot model)
    {
        await using var memoryStream = new MemoryStream();
        await using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 4096, true))
        {
            await fixture.Services.GetRequiredService<ICkSerializer>().SerializeAsync(writer, model);
        }

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }
}
