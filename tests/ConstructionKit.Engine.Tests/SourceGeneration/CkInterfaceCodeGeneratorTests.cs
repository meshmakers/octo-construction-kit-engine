using System.Reflection;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;
using Meshmakers.Octo.ConstructionKit.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SourceGeneration;

/// <summary>
///     F1.4-S1 (AB#5918): C# interfaces for CK interfaces (<c>IRtNamed</c>, <c>extends</c> as interface inheritance)
///     and their explicit implementation on the generated Rt classes. The generated sources are compiled with Roslyn
///     against <c>Runtime.Contracts</c> together with a consumer that assigns <c>RtAccount</c> to <c>IRtNamed</c>.
/// </summary>
public class CkInterfaceCodeGeneratorTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    private const string Ns = "Test.Generated.ckv2KitchenSink.v1";
    private const string Tenant = "gen";

    private (CkCompiledModelRoot Model, CkCacheService Cache) Prepare()
    {
        var model = Model();
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Labeled-1", Extends = [$"{M}/Named-1"],
            // An identical repetition of an inherited member must not be emitted again (CS0108).
            Attributes =
            [
                new() { CkAttributeId = $"{M}/Name", AttributeName = "Name" },
                new() { CkAttributeId = $"{M}/Serial", AttributeName = "Serial", IsOptional = true }
            ]
        });
        model.Interfaces!.Add(new CkInterfaceDto
        {
            InterfaceId = "Named-2", Attributes = [new() { CkAttributeId = $"{M}/Name", AttributeName = "Name" }]
        });
        Type(model, "Tag").Implements = [$"{M}/Labeled-1", $"{M}/Named-2"];
        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));

        var cache = new CkCacheService(NullLogger<CkCacheService>.Instance);
        cache.CreateTenant(Tenant);
        cache.LoadCkModelGraph(Tenant, graph);
        return (model, cache);
    }

    private static Dictionary<string, string> GenerateAll(CkCompiledModelRoot model, CkCacheService cache)
    {
        var sources = new Dictionary<string, string>
        {
            ["CkIds"] = CkIdsCodeGenerator.Instance.Generate(Ns, model.ModelId, model.Types, model.Attributes,
                model.AssociationRoles, model.Records, model.Enums, model.Interfaces)
        };
        foreach (var ckInterface in model.Interfaces!)
        {
            sources[$"I.{ckInterface.InterfaceId.FullName}"] =
                CkInterfaceCodeGenerator.Generate(Ns, model.ModelId, ckInterface, Tenant, cache);
        }

        foreach (var type in model.Types!)
        {
            sources[$"T.{type.TypeId.Name}"] =
                CkTypeCodeGenerator.Instance.Generate(Ns, model.ModelId, type, Tenant, cache);
        }

        return sources;
    }

    [Fact]
    public void Interfaces_AreEmittedWithExtendsAndTypedMembers()
    {
        var (model, cache) = Prepare();
        var sources = GenerateAll(model, cache);

        var named = sources["I.Named-1"];
        Assert.Contains("public partial interface IRtNamed", named);
        Assert.Contains("  string Name { get; }", named);
        Assert.Contains("  string? Description { get; }", named);

        var labeled = sources["I.Labeled-1"];
        Assert.Contains("public partial interface IRtLabeled : IRtNamed", labeled);
        Assert.Contains("  string? Serial { get; }", labeled);
        Assert.DoesNotContain(" Name { get; }", labeled);

        Assert.Contains("public partial interface IRtNamed2", sources["I.Named-2"]);
    }

    [Fact]
    public void RtClasses_ImplementTheirDeclaredInterfacesExplicitly()
    {
        var (model, cache) = Prepare();
        var sources = GenerateAll(model, cache);

        Assert.Contains("public partial class RtPrincipal : RtEntity, IRtNamed", sources["T.Principal"]);
        Assert.Contains("  string IRtNamed.Name => Name;", sources["T.Principal"]);
        Assert.Contains("  string? IRtNamed.Description => default;", sources["T.Principal"]); // not assigned
        Assert.Contains("public partial class RtAccount : RtPrincipal, IRtSerialized", sources["T.Account"]);
        Assert.DoesNotContain("IRtNamed.", sources["T.Account"]); // inherited from RtPrincipal

        var tag = sources["T.Tag"];
        Assert.Contains("public partial class RtTag : RtEntity, IRtLabeled, IRtNamed2", tag);
        Assert.Contains("  string? IRtLabeled.Serial => default;", tag);
        Assert.Contains("  string IRtNamed.Name => Name;", tag);
        Assert.Contains("  string IRtNamed2.Name => Name;", tag);
    }

    [Fact]
    public void GeneratedCode_Compiles_AndAConsumerUsesTheInterfaces()
    {
        var (model, cache) = Prepare();
        var sources = GenerateAll(model, cache);
        sources["Consumer"] = $$"""
            #nullable enable
            namespace {{Ns}};
            public static class Consumer
            {
                public static string Use()
                {
                    IRtNamed named = new RtAccount();
                    IRtLabeled labeled = new RtTag();
                    IRtNamed viaParent = labeled;
                    IRtSerialized serialized = new RtAccount();
                    return named.Name + viaParent.Name + labeled.Serial + serialized.Serial;
                }
            }
            """;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Append(typeof(Meshmakers.Octo.Runtime.Contracts.RepositoryEntities.RtEntity).Assembly)
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .DistinctBy(a => a.Location)
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("GeneratedInterfacesTest",
            sources.Values.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var problems = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error ||
                        d.Severity == DiagnosticSeverity.Warning && d.Id is "CS0108" or "CS8766" or "CS8767" or "CS8613" or "CS8603")
            .ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + Environment.NewLine +
                                         string.Join(Environment.NewLine, sources.Values));
    }

    [Fact]
    public void V1Type_IsUnchanged()
    {
        var (model, cache) = Prepare();
        var tag = model.Types!.Single(t => t.TypeId.Name == "Tag");
        tag.Implements = null;

        var code = CkTypeCodeGenerator.Instance.Generate(Ns, model.ModelId, tag, Tenant, cache);

        Assert.Contains("public partial class RtTag : RtEntity" + Environment.NewLine, code.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        Assert.DoesNotContain("IRt", code);
    }
}
