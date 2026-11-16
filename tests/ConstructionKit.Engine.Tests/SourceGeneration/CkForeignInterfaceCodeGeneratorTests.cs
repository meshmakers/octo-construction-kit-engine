using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Meshmakers.Octo.ConstructionKit.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using KitchenSink = Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.ckv2KitchenSink.Builder;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SourceGeneration;

/// <summary>
///     AB#9096: a type implements interfaces that another CK model declares, and that model's generated C# lives in
///     another assembly. Two-model fixture: <c>ckv2Lib</c> declares <c>Named-1</c> and <c>Describable-1</c> (attribute
///     members) and <c>Hierarchical-1</c> (association member on its own <c>ParentChild</c> role, extends
///     <c>Named-1</c>); <c>ckv2App</c> has an abstract <c>NamedEntity</c> implementing Named-1 and Describable-1, a
///     <c>Tree</c> and a <c>TreeNode</c> implementing Hierarchical-1 with a second ParentChild association to the
///     non-implementor Tree — the shape of Basic 2.7.0 on System (AB#8868). The library is compiled with Roslyn into
///     its own assembly, the application against it.
/// </summary>
public class CkForeignInterfaceCodeGeneratorTests(ITestOutputHelper output)
{
    private const string Lib = "ckv2Lib";
    private const string App = "ckv2App";
    private const string LibNs = "LibAsm.Generated.ckv2Lib.v1";
    private const string AppNs = "AppAsm.Generated.ckv2App.v1";
    private const string Tenant = "foreign";

    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddXUnit(output));

    private static CkCompiledModelRoot LibModel() => new()
    {
        ModelId = new CkModelId(Lib, "1.0.0"),
        CkLanguage = 2,
        Dependencies = [new("System", "1.0.0")],
        Attributes =
        [
            new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
            new() { AttributeId = "Description", ValueType = AttributeValueTypesDto.String }
        ],
        AssociationRoles =
        [
            new()
            {
                AssociationRoleId = "ParentChild", InboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Parent", OutboundName = "Children"
            }
        ],
        Interfaces =
        [
            new()
            {
                InterfaceId = "Named-1",
                Attributes = [new() { CkAttributeId = $"{Lib}/Name", AttributeName = "Name" }]
            },
            new()
            {
                InterfaceId = "Describable-1",
                Attributes = [new() { CkAttributeId = $"{Lib}/Description", AttributeName = "Description", IsOptional = true }]
            },
            new()
            {
                InterfaceId = "Hierarchical-1",
                Extends = [$"{Lib}/Named-1"],
                Associations =
                [
                    new() { CkRoleId = $"{Lib}/ParentChild", TargetCkInterfaceId = $"{Lib}/Hierarchical-1", IsOptional = true }
                ]
            }
        ]
    };

    private static CkCompiledModelRoot AppModel() => new()
    {
        ModelId = new CkModelId(App, "1.0.0"),
        CkLanguage = 2,
        Dependencies = [new("System", "1.0.0"), new(Lib, "1.0.0")],
        Interfaces =
        [
            // An interface of the app model that extends an interface of the library model.
            new()
            {
                InterfaceId = "Labeled-1",
                Extends = [$"{Lib}/Named-1"],
                Attributes = [new() { CkAttributeId = $"{Lib}/Description", AttributeName = "Description", IsOptional = true }]
            }
        ],
        Types =
        [
            new()
            {
                TypeId = "NamedEntity",
                IsAbstract = true,
                Derivable = CkDerivableDto.Any,
                DerivedFromCkTypeId = "System/Entity",
                Implements = [$"{Lib}/Named-1", $"{Lib}/Describable-1"],
                Attributes =
                [
                    new() { CkAttributeId = $"{Lib}/Name", AttributeName = "Name" },
                    new() { CkAttributeId = $"{Lib}/Description", AttributeName = "Description", IsOptional = true }
                ]
            },
            new()
            {
                TypeId = "Tree",
                Derivable = CkDerivableDto.Any,
                DerivedFromCkTypeId = $"{App}/NamedEntity",
                Implements = [$"{App}/Labeled-1"]
            },
            new()
            {
                TypeId = "TreeNode",
                Derivable = CkDerivableDto.Any,
                DerivedFromCkTypeId = $"{App}/NamedEntity",
                Implements = [$"{Lib}/Hierarchical-1"],
                Associations =
                [
                    new() { CkRoleId = $"{Lib}/ParentChild", TargetCkTypeId = $"{App}/TreeNode" },
                    // A second association of the same role to a type that does not implement Hierarchical-1.
                    new() { CkRoleId = $"{Lib}/ParentChild", TargetCkTypeId = $"{App}/Tree" }
                ]
            }
        ]
    };

    private (CkCompiledModelRoot Lib, CkCompiledModelRoot App, CkCacheService Cache) Prepare()
    {
        var lib = LibModel();
        var app = AppModel();
        var operationResult = new OperationResult();
        var modelGraph = new CkModelGraph();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        var originFileResolver = new OriginFileResolver("TEST");
        new ElementResolver().Resolve(lib, modelGraph, new VariableResolver(), originFileResolver, operationResult);
        new ElementResolver().Resolve(app, modelGraph, new VariableResolver(), originFileResolver, operationResult);
        new ReferenceResolver().Resolve(modelGraph, originFileResolver, operationResult);
        new InheritanceResolver(_loggerFactory.CreateLogger<InheritanceResolver>())
            .Resolve(modelGraph, originFileResolver, operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));

        var cache = new CkCacheService(NullLogger<CkCacheService>.Instance);
        cache.CreateTenant(Tenant);
        cache.LoadCkModelGraph(Tenant, modelGraph);
        return (lib, app, cache);
    }

    private static List<string> GenerateModel(string ns, CkCompiledModelRoot model, CkCacheService cache,
        CkGeneratedInterfaceLookup? lookup)
    {
        var sources = new List<string>
        {
            CkIdsCodeGenerator.Instance.Generate(ns, model.ModelId, model.Types, model.Attributes,
                model.AssociationRoles, model.Records, model.Enums, model.Interfaces)
        };
        sources.AddRange((model.Interfaces ?? []).Select(i =>
            CkInterfaceCodeGenerator.Generate(ns, model.ModelId, i, Tenant, cache, lookup)));
        sources.AddRange((model.Types ?? []).Select(t =>
            CkTypeCodeGenerator.Instance.Generate(ns, model.ModelId, t, Tenant, cache, 2, lookup)));
        return sources;
    }

    private static IEnumerable<MetadataReference> BaseReferences() => AppDomain.CurrentDomain.GetAssemblies()
        .Append(typeof(Meshmakers.Octo.Runtime.Contracts.RepositoryEntities.RtEntity).Assembly)
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .DistinctBy(a => a.Location)
        .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location));

    private static CSharpCompilation Compile(string assemblyName, IEnumerable<string> sources,
        IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(assemblyName,
            sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest))),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    private static List<Diagnostic> Problems(Compilation compilation) => compilation
        .GetDiagnostics(TestContext.Current.CancellationToken)
        .Where(d => d.Severity == DiagnosticSeverity.Error ||
                    d.Severity == DiagnosticSeverity.Warning && d.Id is "CS0108" or "CS8766" or "CS8767" or "CS8613" or "CS8603")
        .ToList();

    /// <summary>The library model compiled into its own assembly, as a metadata reference.</summary>
    private static MetadataReference LibAssembly(CkCompiledModelRoot lib, CkCacheService cache)
    {
        var compilation = Compile("LibAsm", GenerateModel(LibNs, lib, cache, null), BaseReferences());
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private const string Consumer = $$"""
        #nullable enable
        namespace {{AppNs}};
        public static class Consumer
        {
            public static string Use()
            {
                global::{{LibNs}}.IRtNamed named = new RtTree();
                global::{{LibNs}}.IRtDescribable describable = new RtTree();
                global::{{LibNs}}.IRtHierarchical hierarchical = new RtTreeNode();
                global::{{LibNs}}.IRtNamed viaParent = hierarchical;
                IRtLabeled labeled = new RtTree();
                global::{{LibNs}}.IRtNamed viaForeignBase = labeled;
                return named.Name + describable.Description + viaParent.Name + labeled.Description + viaForeignBase.Name;
            }
        }
        """;

    [Fact]
    public void ForeignInterfaces_AreFullyQualified_AndTheGeneratedCodeCompilesAgainstTheLibraryAssembly()
    {
        var (lib, app, cache) = Prepare();
        var references = BaseReferences().Append(LibAssembly(lib, cache)).ToList();
        // What the source generator does: read the generated interfaces from the compilation's references.
        var lookup = new CkGeneratedInterfaceLookup(
            CkGeneratedInterfaceCollector.Collect(Compile("AppAsm", [], references), TestContext.Current.CancellationToken));

        var sources = GenerateModel(AppNs, app, cache, lookup);

        Assert.Empty(lookup.Unresolved);
        var namedEntity = sources.Single(s => s.Contains("class RtNamedEntity"));
        Assert.Contains(
            $"public partial class RtNamedEntity : RtEntity, global::{LibNs}.IRtNamed, global::{LibNs}.IRtDescribable",
            namedEntity.Replace("Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities.", ""));
        Assert.Contains($"  string global::{LibNs}.IRtNamed.Name => Name;", namedEntity);
        Assert.Contains($"  string? global::{LibNs}.IRtDescribable.Description => Description;", namedEntity);
        var treeNode = sources.Single(s => s.Contains("class RtTreeNode"));
        Assert.Contains($"public partial class RtTreeNode : RtNamedEntity, global::{LibNs}.IRtHierarchical", treeNode);
        Assert.Contains($"public partial interface IRtLabeled : global::{LibNs}.IRtNamed", sources.Single(s => s.Contains("interface IRtLabeled")));
        var tree = sources.Single(s => s.Contains("class RtTree "));
        Assert.Contains("public partial class RtTree : RtNamedEntity, IRtLabeled", tree); // own-model interface: short name
        Assert.Contains($"  string global::{LibNs}.IRtNamed.Name => Name;", tree); // inherited foreign level

        var problems = Problems(Compile("AppAsm", sources.Append(Consumer), references));
        Assert.True(problems.Count == 0,
            string.Join(Environment.NewLine, problems) + Environment.NewLine + string.Join(Environment.NewLine, sources));
    }

    [Fact]
    public void WithoutTheLookup_ForeignInterfacesStayUnqualified_AndFailWithCS0246()
    {
        // The behaviour before AB#9096 (and of the API without a lookup): the short name does not resolve.
        var (lib, app, cache) = Prepare();
        var references = BaseReferences().Append(LibAssembly(lib, cache)).ToList();

        var sources = GenerateModel(AppNs, app, cache, null);

        Assert.Contains(sources, s => s.Contains("RtEntity, IRtNamed, IRtDescribable"));
        Assert.Contains(Problems(Compile("AppAsm", sources, references)), d => d.Id == "CS0246");
    }

    [Fact]
    public void MissingLibraryReference_IsReportedAsUnresolved()
    {
        var (_, app, cache) = Prepare();
        var lookup = new CkGeneratedInterfaceLookup(
            CkGeneratedInterfaceCollector.Collect(Compile("AppAsm", [], BaseReferences()), TestContext.Current.CancellationToken));

        GenerateModel(AppNs, app, cache, lookup);

        Assert.Equal(
            new[] { "Describable-1", "Hierarchical-1", "Named-1" },
            lookup.Unresolved.Keys.Select(k => k.ElementId.ToString()).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(lookup.Unresolved.Values, reason => Assert.Contains("no referenced assembly", reason));
    }

    [Fact]
    public void Find_ReportsAnAmbiguousInterface_AndMatchesAnEmptyRootNamespace()
    {
        var named = new CkId<CkInterfaceId>(new CkModelId("System", "2.7.0"), "Named-1");
        var describable = new CkId<CkInterfaceId>(new CkModelId("System", "2.7.0"), "Describable-1");
        var hierarchical = new CkId<CkInterfaceId>(new CkModelId("System", "2.7.0"), "Hierarchical-1");
        var lookup = new CkGeneratedInterfaceLookup([
            "A.Generated.System.v2.IRtNamed", "B.Generated.System.v2.IRtNamed", "Generated.System.v2.IRtDescribable",
            "C.Generated.System.v1.IRtHierarchical", "C.Generated.Identity.System.v2.IRtHierarchical"
        ]);

        Assert.Null(lookup.Find(named));
        Assert.Contains("more than one referenced assembly", lookup.Unresolved[named]);
        Assert.Equal("global::Generated.System.v2.IRtDescribable", lookup.Find(describable));
        // Other major, other model name with the same suffix: not a match.
        Assert.Null(lookup.Find(hierarchical));
        Assert.Contains("Meshmakers.Octo.ConstructionKit.Models.System", lookup.Unresolved[hierarchical]);
    }

    [Fact]
    public void OwnModelInterfaces_GenerateByteIdenticalCode_WithTheLookup()
    {
        var model = KitchenSink.Build();
        var modelGraph = new CkModelGraph();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("TEST");
        new ElementResolver().Resolve(model, modelGraph, new VariableResolver(), originFileResolver, operationResult);
        new ReferenceResolver().Resolve(modelGraph, originFileResolver, operationResult);
        new InheritanceResolver(_loggerFactory.CreateLogger<InheritanceResolver>())
            .Resolve(modelGraph, originFileResolver, operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        var cache = new CkCacheService(NullLogger<CkCacheService>.Instance);
        cache.CreateTenant(Tenant);
        cache.LoadCkModelGraph(Tenant, modelGraph);
        const string ns = "Test.Generated.ckv2KitchenSink.v1";
        var lookup = new CkGeneratedInterfaceLookup([$"Other.Generated.{KitchenSink.ModelName}.v1.IRtNamed"]);

        foreach (var type in model.Types!)
        {
            Assert.Equal(CkTypeCodeGenerator.Instance.Generate(ns, model.ModelId, type, Tenant, cache, 2),
                CkTypeCodeGenerator.Instance.Generate(ns, model.ModelId, type, Tenant, cache, 2, lookup));
        }

        foreach (var ckInterface in model.Interfaces!)
        {
            Assert.Equal(CkInterfaceCodeGenerator.Generate(ns, model.ModelId, ckInterface, Tenant, cache),
                CkInterfaceCodeGenerator.Generate(ns, model.ModelId, ckInterface, Tenant, cache, lookup));
        }

        Assert.Empty(lookup.Unresolved);
    }
}
