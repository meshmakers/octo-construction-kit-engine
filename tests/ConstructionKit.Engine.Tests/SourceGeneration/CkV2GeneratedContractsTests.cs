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
///     F1.4-S2 (AB#5919): v2 models get <c>abstract</c> / <c>sealed</c> Rt classes and typed method parameter and
///     result records with a redacting <c>ToString</c>; the generated method names must be unique (L15, message 125).
/// </summary>
public class CkV2GeneratedContractsTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    private const string Ns = "Test.Generated.ckv2KitchenSink.v1";
    private const string Tenant = "gen2";

    private (CkCompiledModelRoot Model, CkCacheService Cache) Prepare(Action<CkCompiledModelRoot>? change = null)
    {
        var model = Model();
        change?.Invoke(model);
        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        var cache = new CkCacheService(NullLogger<CkCacheService>.Instance);
        cache.CreateTenant(Tenant);
        cache.LoadCkModelGraph(Tenant, graph);
        return (model, cache);
    }

    private static string Type(CkCompiledModelRoot model, CkCacheService cache, string name, int ckLanguage) =>
        CkTypeCodeGenerator.Instance.Generate(Ns, model.ModelId, model.Types!.Single(t => t.TypeId.Name == name),
            Tenant, cache, ckLanguage);

    [Fact]
    public void V2Model_AbstractAndSealedClasses()
    {
        var (model, cache) = Prepare(m => Type(m, "Tag").Derivable = CkDerivableDto.Any);

        // Review G3 E-M3: isAbstract stays a plain class (repository generics need new()), and is not sealed.
        Assert.Contains("public partial class RtPrincipal", Type(model, cache, "Principal", 2));
        // derivable: Model (v2 default) without a subtype in the model
        Assert.Contains("public sealed partial class RtAccount", Type(model, cache, "Account", 2));
        // derivable: Any stays open
        Assert.Contains("public partial class RtTag", Type(model, cache, "Tag", 2));
    }

    [Fact]
    public void AbstractV2Type_SatisfiesTheNewConstraint()
    {
        var (model, cache) = Prepare(m => Type(m, "Principal").Derivable = CkDerivableDto.Model);
        var code = Type(model, cache, "Principal", 2);

        Assert.DoesNotContain("abstract", code);
        Assert.DoesNotContain("sealed", code); // isAbstract wins over the derivable rule
    }

    [Fact]
    public void IsFinal_IsSealed_AndAModelSubtypeKeepsTheClassOpen()
    {
        var (model, cache) = Prepare(m =>
        {
            Type(m, "Tag").IsFinal = true;
            Type(m, "Principal").IsAbstract = false;
        });

        Assert.Contains("public sealed partial class RtTag", Type(model, cache, "Tag", 2));
        Assert.Contains("public partial class RtPrincipal", Type(model, cache, "Principal", 2)); // Account derives
    }

    [Fact]
    public void V1Output_IsUnchanged()
    {
        var (model, cache) = Prepare();

        Assert.Contains("public partial class RtPrincipal", Type(model, cache, "Principal", 1));
        Assert.Contains("public partial class RtAccount", Type(model, cache, "Account", 1));
        Assert.Equal(Type(model, cache, "Account", 1),
            CkTypeCodeGenerator.Instance.Generate(Ns, model.ModelId, model.Types!.Single(t => t.TypeId.Name == "Account"),
                Tenant, cache));
    }

    [Fact]
    public void MethodRecords_AreTyped_AndRedactSensitiveParameters()
    {
        var (model, _) = Prepare();

        var code = CkMethodCodeGenerator.Generate(Ns, model.Types!.Single(t => t.TypeId.Name == "Principal"));

        Assert.Contains("public sealed partial record PrincipalChangePasswordParameters", code);
        Assert.Contains("  public string? CurrentPassword { get; init; }", code);
        Assert.Contains("  public required string NewPassword { get; init; }", code);
        Assert.Contains("  public required RtModeEnum Mode { get; init; }", code);
        Assert.Contains("  public required RtAddressRecord Address { get; init; }", code);
        Assert.Contains("public sealed partial record PrincipalChangePasswordResult", code);
        Assert.Contains("  public RtAddressRecord? Value { get; init; }", code);
        Assert.Contains("public sealed partial record PrincipalUnlockParameters", code);
        Assert.DoesNotContain("PrincipalUnlockResult", code);
    }

    [Fact]
    public void GeneratedV2Code_Compiles_AndToStringRedacts()
    {
        var (model, cache) = Prepare();
        var sources = new List<string>
        {
            CkIdsCodeGenerator.Instance.Generate(Ns, model.ModelId, model.Types, model.Attributes,
                model.AssociationRoles, model.Records, model.Enums, model.Interfaces),
            $"namespace {Ns}; public enum RtModeEnum {{ Change, Reset }} public sealed class RtAddressRecord {{ }}"
        };
        sources.AddRange(model.Interfaces!.Select(i => CkInterfaceCodeGenerator.Generate(Ns, model.ModelId, i, Tenant, cache)));
        sources.AddRange(model.Types!.Select(t => CkTypeCodeGenerator.Instance.Generate(Ns, model.ModelId, t, Tenant, cache, 2)));
        sources.AddRange(model.Types!.Where(t => t.Methods is { Count: > 0 }).Select(t => CkMethodCodeGenerator.Generate(Ns, t)));
        sources.Add($$"""
            #nullable enable
            namespace {{Ns}};
            public static class Consumer
            {
                // Review G3 E-M3: the repository generics' constraint must accept the abstract v2 type.
                private static T Make<T>() where T : Meshmakers.Octo.Runtime.Contracts.RepositoryEntities.RtEntity, new() => new T();

                public static string Use()
                {
                    _ = Make<RtPrincipal>();
                    IRtNamed named = new RtAccount();
                    var parameters = new PrincipalChangePasswordParameters
                    {
                        NewPassword = "s3cret", Mode = RtModeEnum.Reset, Address = new RtAddressRecord()
                    };
                    return (named is RtPrincipal ? "" : "?") + parameters;
                }
            }
            """);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Append(typeof(Meshmakers.Octo.Runtime.Contracts.RepositoryEntities.RtEntity).Assembly)
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .DistinctBy(a => a.Location)
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("GeneratedV2ContractsTest",
            sources.Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest),
                cancellationToken: TestContext.Current.CancellationToken)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var rendered = (string)assembly.GetType($"{Ns}.Consumer")!.GetMethod("Use")!.Invoke(null, null)!;
        Assert.DoesNotContain("s3cret", rendered);
        Assert.Contains("NewPassword = ***", rendered);
        Assert.Contains("Mode = Reset", rendered);
    }

    [Fact]
    public void Code125_GeneratedMethodNamesCollide()
    {
        var model = Model();
        model.Types!.Add(new CkCompiledTypeDto
        {
            TypeId = "PrincipalChange", DerivedFromCkTypeId = "System/Entity", Methods = [new() { MethodId = "Password-1" }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 125));
        Assert.Contains("PrincipalChangePassword", message.MessageText);
    }
}
