using System.Security.Cryptography;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#5437: the cascade dry run (<see cref="ICkCascadeService" /> and the surface satisfaction checker behind it)
///     against real compiled models in a local catalog: base model <c>System</c>, the range-retaining dependent
///     <c>PlantR</c>, the exact-pinned dependent <c>PlantE</c> and <c>LineR</c>, which depends on <c>PlantR</c> only
///     (a transitive dependent of System). No network, nothing is published by the dry run.
/// </summary>
public sealed class CkCascadeTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly Dictionary<string, string> NoFiles = new();

    /// <summary>
    ///     System-version: abstract Entity with Name; <paramref name="entityAttributes" /> are extra optional attribute
    ///     assignments on Entity (they must be among <paramref name="attributes" />).
    /// </summary>
    private string WriteSystem(string version, string[] attributes, string[] entityAttributes, string? extraTypeYaml = null,
        string? interfaceYaml = null, int? ckLanguage = null, string entityId = "Entity")
    {
        var files = new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n" +
                                             string.Concat(attributes.Select(a => $"  - id: {a}\n    valueType: String\n")),
            ["types/entity.yaml"] = $"types:\n  - typeId: {entityId}\n    isAbstract: true\n" +
                                    (ckLanguage == 2 ? "    derivable: Any\n" : "") +
                                    (interfaceYaml != null ? "    implements:\n      - ${this}/Named-1\n" : "") +
                                    "    attributes:\n" +
                                    string.Concat(entityAttributes.Select(a =>
                                        $"      - id: ${{this}}/{a}\n        name: {a}\n        isOptional: true\n")) +
                                    (extraTypeYaml ?? "")
        };
        if (interfaceYaml != null)
        {
            files["interfaces/named.yaml"] = interfaceYaml;
        }

        return _fixture.WriteSource($"system-{version}-{Guid.NewGuid():N}", $"System-{version}", null, files, ckLanguage);
    }

    private async Task PublishAsync(string dir, bool rangeRetention)
    {
        _fixture.Services.GetRequiredService<IOptions<CkCompilerOptions>>().Value.RangeRetention = rangeRetention;
        await _fixture.CompileAndPublishAsync(dir);
    }

    private async Task<CkCompiledModelRoot> CompileInMemoryAsync(string dir)
    {
        _fixture.Services.GetRequiredService<IOptions<CkCompilerOptions>>().Value.RangeRetention = false;
        return await _fixture.CompileAsync(dir);
    }

    /// <summary>PlantR / PlantE: a Machine under System/Entity that reuses the System attribute Description.</summary>
    private string WritePlant(string name, string systemRange, params string[] machineAttributes) =>
        _fixture.WriteSource($"{name}-{Guid.NewGuid():N}", $"{name}-1.0.0", [systemRange],
            new Dictionary<string, string>
            {
                ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n",
                ["types/machine.yaml"] = "types:\n  - typeId: Machine\n    derivedFromCkTypeId: ${System}/Entity\n    attributes:\n" +
                                         "      - id: ${System}/Description\n        name: Description\n        isOptional: true\n" +
                                         "      - id: ${this}/Serial\n        name: Serial\n        isOptional: true\n" +
                                         string.Concat(machineAttributes.Select(a =>
                                             $"      - id: ${{System}}/{a}\n        name: {a}\n        isOptional: true\n"))
            });

    private string WriteLine(string plantName) =>
        _fixture.WriteSource($"line-{Guid.NewGuid():N}", "LineR-1.0.0", [$"{plantName}-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["types/line.yaml"] = $"types:\n  - typeId: Line\n    derivedFromCkTypeId: ${{{plantName}}}/Machine\n"
            });

    /// <summary>System-2.2.0 published, then the dependents compiled against it and published.</summary>
    private async Task PublishWorldAsync()
    {
        await PublishAsync(WriteSystem("2.2.0", ["Name", "Description"], ["Name"]), false);
        await PublishAsync(WritePlant("PlantR", "System-[2.2,3.0)"), true);
        await PublishAsync(WritePlant("PlantE", "System-[2.2,3.0)"), false);
        await PublishAsync(WriteLine("PlantR"), true);
    }

    private async Task<CkCascadeResult> AnalyzeAsync(string candidateDir) =>
        await _fixture.Services.GetRequiredService<ICkCascadeService>()
            .AnalyzeAsync(await CompileInMemoryAsync(candidateDir));

    private static CkDependentCheck Dependent(CkCascadeResult result, string name) =>
        Assert.Single(result.Dependents, d => d.Dependent.Name == name);

    private static string Snapshot(string directory) =>
        string.Join("|", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => $"{Path.GetRelativePath(directory, f)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}"));

    [Fact]
    public async Task An_additive_candidate_keeps_range_retaining_dependents_compatible_and_asks_exact_pins_to_repin()
    {
        await PublishWorldAsync();

        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name", "Description", "Remark"], ["Name", "Remark"]));

        Assert.False(result.HasBreaks);
        Assert.Equal(CkDependentVerdict.Compatible, Dependent(result, "PlantR").Verdict);
        Assert.True(Dependent(result, "PlantR").IsRangeRetaining);
        var exact = Dependent(result, "PlantE");
        Assert.Equal(CkDependentVerdict.NeedsRepin, exact.Verdict);
        Assert.False(exact.IsRangeRetaining);
        Assert.Equal(Contracts.SemVer.CkSemVerLevel.Minor, exact.RequiredLevel);
        // LineR depends on System only through PlantR: listed with its own verdict.
        Assert.Contains(result.Dependents, d => d.Dependent.Name == "LineR");
        Assert.Equal(CkDependentVerdict.Compatible, Dependent(result, "LineR").Verdict);
    }

    [Fact]
    public async Task Removing_an_attribute_a_dependent_uses_breaks_it_and_names_the_element()
    {
        await PublishWorldAsync();

        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name"], ["Name"]));

        Assert.True(result.HasBreaks);
        var ranged = Dependent(result, "PlantR");
        Assert.Equal(CkDependentVerdict.Breaks, ranged.Verdict);
        Assert.Contains(ranged.Reasons, r => r.Contains("System@2/Description-1") && r.Contains("does not define"));
        var exact = Dependent(result, "PlantE");
        Assert.Equal(CkDependentVerdict.Breaks, exact.Verdict);
        Assert.Contains(exact.Reasons, r => r.Contains("System-2.2.0/Description-1"));
        // Breaks first in the list
        Assert.Equal(CkDependentVerdict.Breaks, result.Dependents[0].Verdict);
        // The transitive dependent is listed with its own verdict (it references PlantR only).
        var transitive = Dependent(result, "LineR");
        Assert.Equal(CkDependentVerdict.Compatible, transitive.Verdict);
        Assert.Contains(transitive.Reasons, r => r.Contains("builds on PlantR"));
    }

    [Fact]
    public async Task Replay_of_2026_09_30_a_reidentified_base_type_breaks_the_exact_pinned_dependent_naming_the_element()
    {
        await PublishWorldAsync();

        // Entity-1 is re-identified as Entity-2 in the candidate: the pinned reference System-2.2.0/Entity-1 is gone.
        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name", "Description"], ["Name"], entityId: "Entity-2"));

        var exact = Dependent(result, "PlantE");
        Assert.Equal(CkDependentVerdict.Breaks, exact.Verdict);
        Assert.Contains(exact.Reasons, r => r.Contains("System-2.2.0/Entity-1"));
        Assert.Contains(Dependent(result, "PlantR").Reasons, r => r.Contains("System@2/Entity-1"));
    }

    [Fact]
    public async Task A_dependent_whose_range_excludes_the_candidates_major_is_not_in_range()
    {
        await PublishWorldAsync();

        var result = await AnalyzeAsync(WriteSystem("3.0.0", ["Name"], ["Name"]));

        Assert.False(result.HasBreaks);
        Assert.All(result.Dependents, d => Assert.Equal(CkDependentVerdict.NotInRange, d.Verdict));
        Assert.Contains("not in range", Dependent(result, "PlantR").Reasons.Single());
        Assert.Contains("another major", Dependent(result, "PlantE").Reasons.Single());
    }

    [Fact]
    public async Task A_candidate_below_the_floor_of_a_dependent_breaks_it()
    {
        await PublishWorldAsync();

        var result = await AnalyzeAsync(WriteSystem("2.1.0", ["Name", "Description"], ["Name"]));

        var ranged = Dependent(result, "PlantR");
        Assert.Equal(CkDependentVerdict.Breaks, ranged.Verdict);
        Assert.Contains("floor not met", ranged.Reasons.Single());
        Assert.Contains("2.2.0", ranged.Reasons.Single());
        // An exact pin on 2.2.0 is not affected by an older candidate.
        var exact = Dependent(result, "PlantE");
        Assert.Equal(CkDependentVerdict.NotInRange, exact.Verdict);
        Assert.Contains("older than the pinned", exact.Reasons.Single());
    }

    [Fact]
    public async Task A_base_addition_that_collides_with_a_derived_type_member_breaks_the_dependent_H6()
    {
        await PublishWorldAsync();

        // The candidate adds the attribute 'Description' to Entity; PlantR/PlantE Machine already declares it.
        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name", "Description"], ["Name", "Description"]));

        foreach (var name in new[] { "PlantR", "PlantE" })
        {
            var check = Dependent(result, name);
            Assert.Equal(CkDependentVerdict.Breaks, check.Verdict);
            var reason = Assert.Single(check.Reasons);
            Assert.Contains("adds the attribute 'Description'", reason);
            Assert.Contains("System-2.3.0/Entity-1", reason);
            Assert.Contains($"{name}-1.0.0/Machine-1", reason);
        }
    }

    [Fact]
    public async Task A_stable_base_that_redeclares_an_inherited_interface_method_breaks_a_derived_redeclaration_N2()
    {
        const string iface = "interfaces:\n  - interfaceId: Named-1\n    methods:\n      - methodId: Ping-1\n";
        // Baseline: Entity implements Named-1 and inherits Ping-1. PlantR derives from it and redeclares Ping-1.
        await PublishAsync(WriteSystem("2.2.0", ["Name", "Description"], ["Name"], interfaceYaml: iface, ckLanguage: 2), false);
        var plant = _fixture.WriteSource("plantr-n2", "PlantR-1.0.0", ["System-[2.2,3.0)"], new Dictionary<string, string>
        {
            ["types/machine.yaml"] = "types:\n  - typeId: Machine\n    derivedFromCkTypeId: ${System}/Entity\n" +
                                     "    derivable: Any\n    methods:\n      - methodId: Ping-1\n"
        }, 2);
        await PublishAsync(plant, true);

        // Candidate: Entity declares Ping-1 itself as well.
        var candidate = WriteSystem("2.3.0", ["Name", "Description"], ["Name"],
            extraTypeYaml: "    methods:\n      - methodId: Ping-1\n", interfaceYaml: iface,
            ckLanguage: 2);
        var result = await AnalyzeAsync(candidate);

        var check = Dependent(result, "PlantR");
        Assert.Equal(CkDependentVerdict.Breaks, check.Verdict);
        var reason = Assert.Single(check.Reasons);
        Assert.Contains("adds the method 'Ping-1'", reason);
        Assert.Contains("row N2", reason);
        Assert.Contains("error 100", reason);
    }

    [Fact]
    public async Task The_dry_run_is_read_only_and_the_candidate_is_registered_nowhere()
    {
        await PublishWorldAsync();
        var before = Snapshot(_fixture.CatalogDir);

        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name"], ["Name"]));

        Assert.True(result.HasBreaks);
        Assert.Equal(before, Snapshot(_fixture.CatalogDir));
        Assert.False(await _fixture.Services.GetRequiredService<ICatalogService>()
            .IsExistingAsync(new CkModelId("System-2.3.0")));
    }

    [Fact]
    public async Task The_report_lists_every_dependent_with_verdict_and_reason()
    {
        await PublishWorldAsync();
        var result = await AnalyzeAsync(WriteSystem("2.3.0", ["Name"], ["Name"]));

        var markdown = CkCascadeReport.RenderMarkdown(result);
        var console = CkCascadeReport.RenderConsole(result);

        foreach (var dependent in result.Dependents)
        {
            Assert.Contains(dependent.Dependent.FullName, markdown);
            Assert.Contains(dependent.Dependent.FullName, console);
        }

        Assert.Contains("**Breaks**", markdown);
        Assert.Contains("Candidate: System-2.3.0", console);
        Assert.Contains("Baseline: System-2.2.0", markdown);
    }

    [Fact]
    public async Task Without_a_baseline_only_missing_elements_are_detected()
    {
        // No System in any catalog before: the dependents exist but cannot have been compiled; use a first publication.
        var result = await AnalyzeAsync(WriteSystem("1.0.0", ["Name"], ["Name"]));

        Assert.Null(result.Baseline.Baseline);
        Assert.Empty(result.Dependents);
        Assert.Contains("first publication", string.Join("\n", CkCascadeReport.Header(result)));
    }
}
