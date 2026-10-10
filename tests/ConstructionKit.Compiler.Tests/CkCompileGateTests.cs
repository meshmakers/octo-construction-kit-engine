using System.Security.Cryptography;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6294: the compile gate (<see cref="CkCompileGate" />), the decision logic behind <c>CkCompile</c>. The tests
///     mirror <c>CkCompile</c>: they compile a source to a JSON file, load it back with the serializer and hand the
///     loaded model to the gate, against a local file-system catalog in a temp directory plus an in-memory published
///     catalog (standing in for GitHub). No network.
/// </summary>
public sealed class CkCompileGateTests : IDisposable
{
    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _outputCounter;

    public CkCompileGateTests()
    {
        _fixture = new CkCompileFixture(s => s.AddSingleton<ICatalog>(_published));
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>One attribute and one enum; the enum is the part the tests change.</summary>
    private string WriteModel(string version, int? ckLanguage = 2, string enumValues = "A,B")
    {
        var values = string.Concat(enumValues.Split(',').Select((v, i) =>
            $"          - key: {i}\n            name: {v}\n"));
        return _fixture.WriteSource("gate", $"GateModel-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n",
            ["enums/state.yaml"] = "enums:\n  - enumId: State\n    values:\n" + values
        }, ckLanguage);
    }

    private async Task PublishAsync(string sourceDir, string catalogName)
    {
        var compiled = await _fixture.CompileAsync(sourceDir);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(catalogName, compiled,
            new OriginFileResolver(sourceDir), isForced: true);
    }

    /// <summary>Exactly what CkCompile does: compile to a file, load it back, run the gate.</summary>
    private async Task<CkCompileGateResult> RunGateAsync(string sourceDir, CkBaselineSource source)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", $"run{_outputCounter++}");
        var compileResult = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));

        await using var stream = File.OpenRead(compileResult.CompiledModelFile);
        var compiled = await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, compileResult.CompiledModelFile, operationResult);
        return await _fixture.Services.GetRequiredService<CkCompileGate>().RunAsync(compiled, source);
    }

    private static string[] Codes(CkCompileGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    private static string Snapshot(string directory) =>
        string.Join("|", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => $"{Path.GetRelativePath(directory, f)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))}"));

    [Fact]
    public async Task Breaking_change_with_minor_bump_fails_with_one_CK200_naming_the_minimum_version()
    {
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Remote);

        Assert.True(result.HasErrors);
        var error = Assert.Single(result.Messages, m => m.Severity == CkCompileGateSeverity.Error);
        Assert.Equal("OCTO-CK200", error.Code);
        Assert.Contains("B", error.Text);
        Assert.Contains("MAJOR", error.Text);
        Assert.Contains("3.0.0", error.Text);
        Assert.Contains("GateModel-3.0.0", error.Text);
    }

    [Fact]
    public async Task Breaking_change_with_major_bump_passes()
    {
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(WriteModel("3.0.0", enumValues: "A"), CkBaselineSource.Remote);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Verdict);
        Assert.Contains(result.Messages, m => m.Text.Contains("baseline GateModel-2.4.0"));
    }

    [Fact]
    public async Task Additive_change_with_minor_bump_passes_and_logs_the_verdict_line()
    {
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A,B,C"), CkBaselineSource.Remote);

        Assert.False(result.HasErrors);
        var line = Assert.Single(result.Messages).Text;
        Assert.Contains("model 'GateModel'", line);
        Assert.Contains("declared 2.5.0", line);
        Assert.Contains("baseline GateModel-2.4.0 (" + InMemoryPublishedCatalog.Name + ")", line);
        Assert.Contains("required level MINOR", line);
    }

    [Fact]
    public async Task Every_uncovered_change_gets_its_own_CK200()
    {
        await PublishAsync(WriteModel("2.4.0", enumValues: "A,B,C"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.4.1", enumValues: "A"), CkBaselineSource.Remote);

        Assert.Equal(["OCTO-CK200", "OCTO-CK200"], Codes(result));
    }

    [Fact]
    public async Task Declared_version_below_the_published_version_fails_with_CK201()
    {
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.3.0"), CkBaselineSource.Remote);

        Assert.Equal(["OCTO-CK201"], Codes(result));
    }

    [Fact]
    public async Task Local_entries_at_or_above_the_declared_version_are_never_the_baseline()
    {
        // The baseline 2.4.0 was built locally earlier (a published copy is not needed in Local mode).
        await PublishAsync(WriteModel("2.4.0"), LocalFileSystemCatalog.Name);

        // Build 1: additive at 2.5.0. CkCompile then publishes the model to the local catalog.
        var additive = WriteModel("2.5.0", enumValues: "A,B,C");
        var first = await RunGateAsync(additive, CkBaselineSource.Local);
        Assert.False(first.HasErrors);
        await PublishAsync(additive, LocalFileSystemCatalog.Name);

        // Build 2 (same model again) and build 3 (second target framework): same baseline, same verdict.
        var second = await RunGateAsync(additive, CkBaselineSource.Local);
        var third = await RunGateAsync(additive, CkBaselineSource.Local);
        Assert.False(second.HasErrors);
        Assert.False(third.HasErrors);
        Assert.Equal("GateModel-2.4.0", first.Verdict!.BaselineId.FullName);
        Assert.Equal("GateModel-2.4.0", second.Verdict!.BaselineId.FullName);
        Assert.Equal("GateModel-2.4.0", third.Verdict!.BaselineId.FullName);
        Assert.Equal(first.Messages, second.Messages);

        // A breaking edit at the unchanged version still fails although 2.5.0 is now in the local catalog.
        var broken = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Local);
        Assert.Equal(["OCTO-CK200"], Codes(broken));
        Assert.Equal("GateModel-2.4.0", broken.Verdict!.BaselineId.FullName);
    }

    [Fact]
    public async Task The_gate_writes_to_no_catalog()
    {
        await PublishAsync(WriteModel("2.4.0"), LocalFileSystemCatalog.Name);
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);
        var before = Snapshot(_fixture.CatalogDir);

        var failing = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Local);
        var passing = await RunGateAsync(WriteModel("2.5.0", enumValues: "A,B,C"), CkBaselineSource.Local);
        var remote = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Remote);

        Assert.True(failing.HasErrors);
        Assert.False(passing.HasErrors);
        Assert.True(remote.HasErrors);
        Assert.Equal(before, Snapshot(_fixture.CatalogDir));
        var published = await _published.IsExistingAsync(new CkModelIdVersionRange("GateModel", "[2.5.0,2.6.0)"));
        Assert.False(published.Exists);
    }

    [Fact]
    public async Task Local_baseline_needs_no_remote_catalog_and_names_the_local_entry()
    {
        // Only the local catalog knows the model; every remote catalog is disabled by the fixture.
        await PublishAsync(WriteModel("2.4.0"), LocalFileSystemCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Local);

        Assert.Equal(["OCTO-CK200"], Codes(result));
        Assert.Contains(result.Messages, m => m.Text.Contains("local, not published"));
    }

    [Fact]
    public async Task Remote_baseline_ignores_the_local_catalog()
    {
        await PublishAsync(WriteModel("2.4.0"), LocalFileSystemCatalog.Name);

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Remote);

        Assert.False(result.HasErrors);
        Assert.Null(result.Verdict);
        Assert.Contains("first publication", Assert.Single(result.Messages).Text);
    }

    [Fact]
    public async Task Unreachable_source_without_baseline_is_an_error_in_Remote_and_information_in_Local()
    {
        _published.SourceUnreachable = true;

        var remote = await RunGateAsync(WriteModel("2.5.0"), CkBaselineSource.Remote);
        var local = await RunGateAsync(WriteModel("2.5.0"), CkBaselineSource.Local);

        Assert.Equal(["OCTO-CK202"], Codes(remote));
        Assert.False(local.HasErrors);
        var message = Assert.Single(local.Messages);
        Assert.Equal("OCTO-CK202", message.Code);
        Assert.Equal(CkCompileGateSeverity.Info, message.Severity);
    }

    [Fact]
    public async Task Unreachable_source_with_a_cached_baseline_still_runs_the_gate_and_notes_the_stale_cache()
    {
        await PublishAsync(WriteModel("2.4.0"), InMemoryPublishedCatalog.Name);
        _published.SourceUnreachable = true;

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A"), CkBaselineSource.Remote);

        Assert.Equal(["OCTO-CK200"], Codes(result));
        Assert.Contains(result.Messages, m => m.Text.Contains("may be stale"));
    }

    [Fact]
    public async Task First_publication_passes()
    {
        var result = await RunGateAsync(WriteModel("1.0.0"), CkBaselineSource.Remote);

        Assert.False(result.HasErrors);
        Assert.Contains("first publication", Assert.Single(result.Messages).Text);
    }

    [Fact]
    public async Task A_ckLanguage_1_model_only_gets_the_information_line()
    {
        await PublishAsync(WriteModel("2.4.0", ckLanguage: null), InMemoryPublishedCatalog.Name);

        var breaking = await RunGateAsync(WriteModel("2.5.0", ckLanguage: null, enumValues: "A"),
            CkBaselineSource.Remote);
        var downgrade = await RunGateAsync(WriteModel("2.3.0", ckLanguage: null), CkBaselineSource.Remote);

        foreach (var result in new[] { breaking, downgrade })
        {
            Assert.False(result.HasErrors);
            var message = Assert.Single(result.Messages);
            Assert.Equal(CkCompileGateSeverity.Info, message.Severity);
            Assert.Null(message.Code);
            Assert.StartsWith("Compatibility: model 'GateModel'", message.Text);
        }
    }

    [Fact]
    public async Task A_failing_check_never_breaks_a_ckLanguage_1_build_but_does_break_a_ckLanguage_2_build()
    {
        var resolver = A.Fake<ICkBaselineResolver>();
        A.CallTo(() => resolver.ResolveAsync(A<string>._, A<CkVersion>._, A<CkBaselineSource>._))
            .Throws(new InvalidOperationException("catalog exploded"));
        var gate = new CkCompileGate(resolver, A.Fake<ICkCompatibilityVerdictService>());

        var v1 = await gate.RunAsync(await _fixture.CompileAsync(WriteModel("1.0.0", ckLanguage: null)),
            CkBaselineSource.Local);
        Assert.False(v1.HasErrors);
        Assert.Contains("skipped", Assert.Single(v1.Messages).Text);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await gate.RunAsync(await _fixture.CompileAsync(WriteModel("1.0.0")), CkBaselineSource.Local));
    }

    [Theory]
    [InlineData(null, false, CkBaselineSource.Local)]
    [InlineData("", true, CkBaselineSource.Remote)]
    [InlineData("  ", false, CkBaselineSource.Local)]
    [InlineData(null, true, CkBaselineSource.Remote)]
    [InlineData("Local", true, CkBaselineSource.Local)]
    [InlineData("remote", false, CkBaselineSource.Remote)]
    public void The_property_overrides_the_CI_default(string? value, bool isContinuousIntegration,
        CkBaselineSource expected)
    {
        Assert.True(CkCompileGate.TryGetBaselineSource(value, isContinuousIntegration, out var source));
        Assert.Equal(expected, source);
    }

    [Fact]
    public void An_unknown_baseline_source_is_rejected()
    {
        Assert.False(CkCompileGate.TryGetBaselineSource("Nearby", false, out _));
    }
}
