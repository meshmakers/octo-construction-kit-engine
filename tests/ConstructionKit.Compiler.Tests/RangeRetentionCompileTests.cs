using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#5664 / AB#5665 (CK v2 Phase 0 spike): the compiler keeps the declared dependency range and floor and
///     writes major-qualified references when <see cref="CkCompilerOptions.RangeRetention" /> is on; a model
///     compiled that way resolves against any later minor of its dependency.
/// </summary>
public sealed class RangeRetentionCompileTests : IDisposable
{
    private readonly CkCompileFixture _on =
        new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = true));

    private readonly CkCompileFixture _off =
        new(s => s.Configure<CkCompilerOptions>(o => o.RangeRetention = false));

    public void Dispose()
    {
        _on.Dispose();
        _off.Dispose();
    }

    private static string DependentSource(CkCompileFixture fixture, string range, bool useExtra = false,
        string modelId = "Dep-1.0.0") =>
        fixture.WriteSource($"dep-{Guid.NewGuid():N}", modelId, [range], new Dictionary<string, string>
        {
            ["attributes/a.yaml"] = "attributes:\n  - id: Code\n    valueType: String\n",
            ["types/thing.yaml"] =
                "types:\n  - typeId: Thing\n    derivedFromCkTypeId: ${System}/Entity\n    attributes:\n" +
                "      - id: ${this}/Code\n        name: Code\n" +
                (useExtra ? "      - id: ${System}/Extra\n        name: Extra\n        isOptional: true\n" : "")
        });

    private async Task PublishSystemsAsync(CkCompileFixture fixture, params string[] versions)
    {
        foreach (var version in versions)
        {
            // 2.5.0 and later carry the additive optional attribute "Extra".
            await fixture.CompileAndPublishAsync(
                fixture.WriteSystemModel(version, withExtraAttribute: new CkVersion(version).Minor >= 5));
        }
    }

    // AB#6675: range retention is the compiler default - no property, option or environment variable is needed.
    [Fact]
    public async Task Default_WithoutAnyFlag_WritesRangesFloorsAndMajorQualifiedReferences()
    {
        Assert.True(new CkCompilerOptions().RangeRetention);
        using var byDefault = new CkCompileFixture(s =>
            s.Configure<CkCompilerOptions>(o => o.RangeRetention = new CkCompilerOptions().RangeRetention));
        await byDefault.CompileAndPublishAsync(byDefault.WriteSystemModel("2.5.0", withExtraAttribute: true));

        var compiled = await byDefault.CompileAsync(DependentSource(byDefault, "System-[2.5,3.0)"));

        var dependency = Assert.Single(compiled.DependencyRanges!);
        Assert.Equal("System-[2.5,3.0)", dependency.Range.FullName);
        Assert.Equal("2.5.0", dependency.Floor);
        Assert.Equal("System@2/Entity-1", compiled.Types!.Single().DerivedFromCkTypeId!.FullName);
    }

    [Fact]
    public async Task FlagOff_OutputKeepsExactPinsAndConcreteReferences()
    {
        await PublishSystemsAsync(_off, "2.4.0", "2.5.0");

        var compiled = await _off.CompileAsync(DependentSource(_off, "System-[2.4,3.0)"));

        Assert.Null(compiled.DependencyRanges);
        Assert.Equal(["System-2.5.0"], compiled.Dependencies!.Select(d => d.FullName));
        Assert.Equal("System-2.5.0/Entity-1", compiled.Types!.Single().DerivedFromCkTypeId!.FullName);
        Assert.DoesNotContain("dependencyRanges", await ToYamlAsync(_off, compiled));
    }

    [Fact]
    public async Task FlagOn_OutputKeepsRangeAndFloorAndMajorQualifiedReferences()
    {
        await PublishSystemsAsync(_on, "2.4.0", "2.5.0");

        var compiled = await _on.CompileAsync(DependentSource(_on, "System-[2.4,3.0)"));

        var dependency = Assert.Single(compiled.DependencyRanges!);
        Assert.Equal("System-[2.4,3.0)", dependency.Range.FullName);
        Assert.Equal("2.4.0", dependency.Floor);
        Assert.Equal("System@2/Entity-1", compiled.Types!.Single().DerivedFromCkTypeId!.FullName);
        // Own references stay concrete; the legacy closure stays for engines without range retention.
        Assert.Equal("Dep-1.0.0/Code-1", compiled.Types!.Single().Attributes!.Single().CkAttributeId.FullName);
        Assert.Equal(["System-2.5.0"], compiled.Dependencies!.Select(d => d.FullName));
    }

    [Fact]
    public async Task FlagOn_CompiledYamlRoundTripsThroughSchemaValidation()
    {
        await PublishSystemsAsync(_on, "2.5.0");
        var outputDir = Path.Combine(_on.Root, "out");
        Directory.CreateDirectory(outputDir);

        var result = await _on.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(DependentSource(_on, "System-[2.5,3.0)"), outputDir, null);

        var yaml = await File.ReadAllTextAsync(result.CompiledModelFile, TestContext.Current.CancellationToken);
        Assert.Contains("dependencyRanges:", yaml);
        Assert.Contains("System@2/Entity-1", yaml);
        var operationResult = new OperationResult();
        var readBack = await _on.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(yaml, result.CompiledModelFile, operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        Assert.Equal("2.5.0", readBack.DependencyRanges!.Single().Floor);
        Assert.True(readBack.Types!.Single().DerivedFromCkTypeId!.ModelId.IsMajorQualified);
    }

    [Fact]
    public async Task FlagOn_ReferenceMissingAtFloor_FailsWithClearMessage()
    {
        await PublishSystemsAsync(_on, "2.4.0", "2.5.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            _on.CompileAsync(DependentSource(_on, "System-[2.4,3.0)", useExtra: true)));

        Assert.Contains("attribute System/Extra-1", exception.Message);
        Assert.Contains("System-[2.4,3.0)", exception.Message);
        Assert.Contains("missing in 'System-2.4.0'", exception.Message);
    }

    [Fact]
    public async Task FlagOn_ReferenceExistingAtFloor_Compiles()
    {
        await PublishSystemsAsync(_on, "2.4.0", "2.5.0");

        var compiled = await _on.CompileAsync(DependentSource(_on, "System-[2.5,3.0)", useExtra: true));

        Assert.Equal("2.5.0", compiled.DependencyRanges!.Single().Floor);
    }

    [Fact]
    public async Task FloorNeverPublished_VerifiesAgainstLowestAvailableVersionInRange()
    {
        // Range [2.0,3.0) but the catalog starts at 2.4.0: 2.4.0 is the oldest a tenant can have.
        await PublishSystemsAsync(_on, "2.4.0", "2.5.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            _on.CompileAsync(DependentSource(_on, "System-[2.0,3.0)", useExtra: true)));

        Assert.Contains("missing in 'System-2.4.0'", exception.Message);
    }

    [Fact]
    public async Task RangeRetainingModel_ResolvesAgainstALaterMinorWithoutRecompile()
    {
        await PublishSystemsAsync(_on, "2.5.0");
        var dependent = await _on.CompileAndPublishAsync(DependentSource(_on, "System-[2.5,3.0)"));

        // System moves on; the dependent is NOT recompiled.
        await PublishSystemsAsync(_on, "2.6.0");
        var graph = await _on.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(dependent, new OriginFileResolver("-"), new OperationResult());

        Assert.Contains(graph.Models.Keys, m => m.FullName == "System-2.6.0");
        var thing = graph.Types[new CkId<CkTypeId>("Dep-1.0.0/Thing-1")];
        Assert.Equal("System-2.6.0/Entity-1", thing.DerivedFromCkTypeId!.FullName);
        // The caller's instance keeps its version-less form (it may be persisted afterwards).
        Assert.Equal("System@2/Entity-1", dependent.Types!.Single().DerivedFromCkTypeId!.FullName);
    }

    [Fact]
    public async Task RangeRetainingChain_SecondLevelDependentResolvesThroughRanges()
    {
        await PublishSystemsAsync(_on, "2.5.0");
        await _on.CompileAndPublishAsync(DependentSource(_on, "System-[2.5,3.0)"));
        var level2Source = _on.WriteSource("level2", "Level2-1.0.0", ["System-[2.5,3.0)", "Dep-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["types/special.yaml"] = "types:\n  - typeId: Special\n    derivedFromCkTypeId: ${Dep}/Thing\n"
            });
        var level2 = await _on.CompileAndPublishAsync(level2Source);
        Assert.Equal("Dep@1/Thing-1", level2.Types!.Single().DerivedFromCkTypeId!.FullName);

        await PublishSystemsAsync(_on, "2.6.0");
        var graph = await _on.Services.GetRequiredService<ICatalogModelResolver>()
            .HardResolveAsync(level2, new OriginFileResolver("-"), new OperationResult());

        Assert.Contains(graph.Models.Keys, m => m.FullName == "System-2.6.0");
        Assert.DoesNotContain(graph.Models.Keys, m => m.FullName == "System-2.5.0");
    }

    [Theory]
    [InlineData("System-2.5")]
    [InlineData("System-[2.0,)")]
    [InlineData("System-[2.5,3.0]")]
    [InlineData("System-[2.5,4.0)")]
    public async Task FlagOn_RangeSpanningSeveralMajors_IsRejected(string range)
    {
        await PublishSystemsAsync(_on, "2.5.0");

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            _on.CompileAsync(DependentSource(_on, range)));

        Assert.Contains("more than one major", exception.Message);
        Assert.Contains(range, exception.Message);
    }

    [Fact]
    public async Task FlagOff_RangeSpanningSeveralMajors_StillCompiles()
    {
        await PublishSystemsAsync(_off, "2.5.0");

        var compiled = await _off.CompileAsync(DependentSource(_off, "System-[2.0,)"));

        Assert.Equal(["System-2.5.0"], compiled.Dependencies!.Select(d => d.FullName));
    }

    [Fact]
    public async Task FlagOn_ExclusiveLowerBound_FloorIsInsideTheRange()
    {
        await PublishSystemsAsync(_on, "2.4.0", "2.4.1", "2.5.0");

        var compiled = await _on.CompileAsync(DependentSource(_on, "System-(2.4,3.0)"));

        Assert.Equal("2.4.1", compiled.DependencyRanges!.Single().Floor);
    }

    private async Task PublishClassicBotAsync()
    {
        await PublishSystemsAsync(_on, "2.5.0");
        await PublishSystemsAsync(_off, "2.5.0");
        var classic = await _off.CompileAsync(_off.WriteSource("bot", "Bot-1.0.0", ["System-[2.5,3.0)"],
            new Dictionary<string, string>
            {
                ["types/b.yaml"] = "types:\n  - typeId: BotThing\n    derivedFromCkTypeId: ${System}/Entity\n"
            }));
        Assert.Equal(["System-2.5.0"], classic.Dependencies!.Select(d => d.FullName));
        await _on.Services.GetRequiredService<ICatalogService>().PublishAsync(
            Engine.ModelCatalogs.LocalFileSystemCatalog.Name, classic, new OriginFileResolver("bot"), true);
        // The classic build lane (_off) needs Bot as well to compile classic dependents of it (Mid).
        await _off.Services.GetRequiredService<ICatalogService>().PublishAsync(
            Engine.ModelCatalogs.LocalFileSystemCatalog.Name, classic, new OriginFileResolver("bot"), true);
        await PublishSystemsAsync(_on, "2.6.0");
    }

    private string MixedSource(string systemRange) =>
        _on.WriteSource("mixed", "Mixed-1.0.0", [systemRange, "Bot-[1.0,2.0)"], new Dictionary<string, string>
        {
            ["types/m.yaml"] = "types:\n  - typeId: Mixed\n    derivedFromCkTypeId: ${Bot}/BotThing\n"
        });

    // AB#6671 (was review N4): a classic exact pin (Bot pins System-2.5.0) and a range on the same model resolve to
    // ONE version that satisfies both, although the catalog holds a newer minor.
    [Fact]
    public async Task FlagOn_ClassicExactPinAndRangeOnSameModel_ResolveSystemOnce()
    {
        await PublishClassicBotAsync();

        var compiled = await _on.CompileAsync(MixedSource("System-[2.5,3.0)"));

        Assert.Equal(["Bot-1.0.0", "System-2.5.0"], compiled.Dependencies!.Select(d => d.FullName).Order());
        var systemRange = compiled.DependencyRanges!.Single(r => r.Range.Name == "System");
        Assert.Equal("System-[2.5,3.0)", systemRange.Range.FullName);
        Assert.Equal("2.5.0", systemRange.Floor);
        Assert.Equal("Bot@1/BotThing-1", compiled.Types!.Single().DerivedFromCkTypeId!.FullName);
    }

    // AB#6671: no version satisfies [2.6,3.0) and the exact pin 2.5.0 together - the message names both
    // constraints, who requires them and the visible versions.
    [Fact]
    public async Task FlagOn_ConstraintsNoVersionSatisfies_FailsAndNamesConstraintsAndVersions()
    {
        await PublishClassicBotAsync();

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            _on.CompileAsync(MixedSource("System-[2.6,3.0)")));

        Assert.Contains("No version of construction kit model 'System' satisfies all dependency constraints",
            exception.Message);
        Assert.Contains("'System-[2.6,3.0)' (required by the model being resolved)", exception.Message);
        Assert.Contains("'System-[2.5.0]' (required by 'Bot-1.0.0')", exception.Message);
        Assert.Contains("2.5.0, 2.6.0", exception.Message);
    }

    // AB#6671: three levels - Top (range retaining) -> Mid (classic, pins Bot and System) -> Bot (classic, pins System).
    [Fact]
    public async Task FlagOn_ThreeLevelChainWithExactPins_ResolvesEveryModelOnce()
    {
        await PublishClassicBotAsync();
        await _on.Services.GetRequiredService<ICatalogService>().PublishAsync(
            Engine.ModelCatalogs.LocalFileSystemCatalog.Name,
            await _off.CompileAsync(_off.WriteSource("mid", "Mid-1.0.0", ["System-[2.5,3.0)", "Bot-[1.0,2.0)"],
                new Dictionary<string, string>
                {
                    ["types/m.yaml"] = "types:\n  - typeId: MidThing\n    derivedFromCkTypeId: ${Bot}/BotThing\n"
                })), new OriginFileResolver("mid"), true);
        var top = _on.WriteSource("top", "Top-1.0.0", ["System-[2.5,3.0)", "Mid-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["types/t.yaml"] = "types:\n  - typeId: TopThing\n    derivedFromCkTypeId: ${Mid}/MidThing\n"
            });

        var compiled = await _on.CompileAsync(top);

        Assert.Equal(["Bot-1.0.0", "Mid-1.0.0", "System-2.5.0"], compiled.Dependencies!.Select(d => d.FullName).Order());
    }

    // AB#6671: two ranges on one model (Lib-[1.0,1.5) of the root, Lib-[1.2,2.0) of a dependency) resolve to the
    // highest version inside both (1.4), not to 1.4 and 1.8 (error 66 before).
    [Fact]
    public async Task FlagOn_TwoRangesOnSameModel_ResolveToHighestVersionInBoth()
    {
        foreach (var version in new[] { "1.0.0", "1.4.0", "1.8.0" })
        {
            await _on.CompileAndPublishAsync(_on.WriteSource($"lib-{version}", $"Lib-{version}", [],
                new Dictionary<string, string> { ["attributes/a.yaml"] = "attributes:\n  - id: Code\n    valueType: String\n" }));
        }

        await _on.CompileAndPublishAsync(_on.WriteSource("mid2", "Mid2-1.0.0", ["Lib-[1.2,2.0)"],
            new Dictionary<string, string> { ["attributes/b.yaml"] = "attributes:\n  - id: Other\n    valueType: String\n" }));
        var compiled = await _on.CompileAsync(_on.WriteSource("root2", "Root2-1.0.0", ["Lib-[1.0,1.5)", "Mid2-[1.0,2.0)"],
            new Dictionary<string, string> { ["attributes/c.yaml"] = "attributes:\n  - id: Third\n    valueType: String\n" }));

        Assert.Equal(["Lib-1.4.0", "Mid2-1.0.0"], compiled.Dependencies!.Select(d => d.FullName).Order());
    }

    // Review (CodeRabbit on #273): every round of the solver discovers one more level of the closure, so a deep acyclic
    // chain must not run into a round limit.
    [Fact]
    public async Task FlagOn_DeepDependencyChain_ResolvesWithoutRoundLimit()
    {
        const int depth = 80;
        for (var i = 0; i < depth; i++)
        {
            await _on.CompileAndPublishAsync(_on.WriteSource($"chain{i}", $"Chain{i}-1.0.0",
                i == 0 ? [] : [$"Chain{i - 1}-[1.0,2.0)"],
                new Dictionary<string, string>
                {
                    ["attributes/a.yaml"] = "attributes:\n  - id: Code\n    valueType: String\n"
                }));
        }

        var compiled = await _on.CompileAsync(_on.WriteSource("chaintop", "ChainTop-1.0.0", [$"Chain{depth - 1}-[1.0,2.0)"],
            new Dictionary<string, string>
            {
                ["attributes/a.yaml"] = "attributes:\n  - id: Code\n    valueType: String\n"
            }));

        Assert.Equal(depth, compiled.Dependencies!.Count);
    }

    // AB#5435 (Thor's check): usedSurface / usedSurfaceHash are computed from the elements the model uses, not from the
    // dependency's content or version, so a rebuild against a later System minor yields the identical surface.
    [Fact]
    public async Task FlagOn_UsedSurface_IsIndependentOfTheResolvedDependencyVersion()
    {
        await PublishSystemsAsync(_on, "2.5.0");
        var source = DependentSource(_on, "System-[2.5,3.0)");
        var before = await _on.CompileAsync(source);

        await PublishSystemsAsync(_on, "2.6.0");
        var after = await _on.CompileAsync(source);

        Assert.Equal(["System-2.5.0"], before.Dependencies!.Select(d => d.FullName));
        Assert.Equal(["System-2.6.0"], after.Dependencies!.Select(d => d.FullName)); // the closure moved ...
        var surfaceBefore = before.DependencyRanges!.Single();
        var surfaceAfter = after.DependencyRanges!.Single();
        Assert.NotNull(surfaceBefore.UsedSurface);
        Assert.Equal(surfaceBefore.UsedSurface, surfaceAfter.UsedSurface); // ... the surface did not
        Assert.Equal(surfaceBefore.UsedSurfaceHash, surfaceAfter.UsedSurfaceHash);
        Assert.Equal(surfaceBefore.Floor, surfaceAfter.Floor);
    }

    // The solver takes the HIGHEST version that satisfies all constraints when several do.
    [Fact]
    public async Task FlagOn_SeveralVersionsSatisfyAllConstraints_HighestWins()
    {
        await PublishSystemsAsync(_on, "2.5.0", "2.6.0", "2.7.0");
        var compiled = await _on.CompileAsync(DependentSource(_on, "System-[2.5,3.0)"));
        Assert.Equal(["System-2.7.0"], compiled.Dependencies!.Select(d => d.FullName));
    }

    // Review L14: references into a TRANSITIVE dependency are floor-checked against the floor the intermediate
    // model guarantees (Mid declares System-[2.4,3.0), Top declares only Mid but uses System elements).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FlagOn_TransitiveReference_IsFloorCheckedAgainstTheIntermediateRange(bool useExtra)
    {
        await PublishSystemsAsync(_on, "2.4.0", "2.5.0");
        await _on.CompileAndPublishAsync(_on.WriteSource("mid", "Mid-1.0.0", ["System-[2.4,3.0)"],
            new Dictionary<string, string>
            {
                ["types/m.yaml"] = "types:\n  - typeId: MidThing\n    derivedFromCkTypeId: ${System}/Entity\n"
            }));
        var top = _on.WriteSource("top", "Top-1.0.0", ["Mid-[1.0,2.0)"], new Dictionary<string, string>
        {
            ["types/t.yaml"] = "types:\n  - typeId: TopThing\n    derivedFromCkTypeId: ${Mid}/MidThing\n" +
                               (useExtra
                                   ? "    attributes:\n      - id: ${System}/Extra\n        name: Extra\n        isOptional: true\n"
                                   : "")
        });

        if (useExtra)
        {
            var exception = await Assert.ThrowsAsync<ModelValidationException>(() => _on.CompileAsync(top));
            Assert.Contains("attribute System/Extra-1", exception.Message);
            Assert.Contains("missing in 'System-2.4.0'", exception.Message);
            Assert.Contains("transitive", exception.Message);
            Assert.Contains("Mid-1.0.0", exception.Message);
        }
        else
        {
            var compiled = await _on.CompileAsync(top);
            Assert.Equal("Mid-[1.0,2.0)", compiled.DependencyRanges!.Single().Range.FullName);
        }
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
