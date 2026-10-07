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
