using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#7894: a published Minor with <c>compatibility.acknowledge</c> entries must be rebuildable. Round trip over both
///     gates against one published catalog: publish 2.4.0, publish the Minor 2.5.0 that tightens a security-sensitive
///     attribute with an acknowledgement, then rebuild 2.5.0 (the CI rebuild of the released commit) - with the entries,
///     without them, and with a structural change but no version raise.
/// </summary>
/// <remarks>
///     Before the fix the compile gate took the published 2.5.0 as its own baseline and judged every entry stale
///     (<c>OCTO-CK204</c>, "remove the entry"), and the publish gate refused the rebuild without the entries
///     (<c>OCTO-CK205</c>, the acknowledgements counted as content). System.Identity 2.28.0 and 3.1.0 were blocked that way.
/// </remarks>
public sealed class CkAcknowledgeRebuildRoundTripTests : IDisposable
{
    private const string SecretKey = "RecordAttribute:Login-1/Secret#Modified:access";
    private const string Reason = "Close accepted risk R13: secret readable via GraphQL";

    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _outputCounter;

    public CkAcknowledgeRebuildRoundTripTests()
    {
        _fixture = new CkCompileFixture(s => s.AddSingleton<ICatalog>(_published));
    }

    public void Dispose() => _fixture.Dispose();

    private static string Acknowledge(params (string Change, string Reason)[] entries) =>
        entries.Length == 0
            ? ""
            : "compatibility:\n  acknowledge:\n" + string.Concat(entries.Select(e =>
                $"    - change: \"{e.Change}\"\n      reason: \"{e.Reason}\"\n"));

    private string WriteModel(string version, string access = "ReadOnly", string enumValues = "A,B",
        string? acknowledge = null)
    {
        var values = string.Concat(enumValues.Split(',').Select((v, i) =>
            $"          - key: {i}\n            name: {v}\n"));
        return _fixture.WriteSource("gate", $"GateModel-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] =
                "attributes:\n  - id: Secret\n    valueType: String\n    securitySensitive: true\n",
            ["records/login.yaml"] = "records:\n  - recordId: Login\n    attributes:\n      - id: ${this}/Secret\n" +
                                     $"        name: Secret\n        isOptional: true\n        access: {access}\n",
            ["enums/state.yaml"] = "enums:\n  - enumId: State\n    values:\n" + values
        }, 2, acknowledge);
    }

    private async Task<CkCompiledModelRoot> CompileToFileAndBackAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var output = Path.Combine(_fixture.Root, "out", $"run{_outputCounter++}");
        var compileResult = await _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileAsync(sourceDir, output, null, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));
        await using var stream = File.OpenRead(compileResult.CompiledModelFile);
        return await _fixture.Services.GetRequiredService<ICkSerializer>()
            .DeserializeCompiledModelRootAsync(stream, compileResult.CompiledModelFile, operationResult);
    }

    private async Task<CkCompileGateResult> RunCompileGateAsync(CkCompiledModelRoot model) =>
        await _fixture.Services.GetRequiredService<CkCompileGate>().RunAsync(model, CkBaselineSource.Remote);

    private async Task<CkPublishGateResult> RunPublishGateAsync(CkCompiledModelRoot model) =>
        await _fixture.Services.GetRequiredService<CkPublishGate>().EvaluateAsync(InMemoryPublishedCatalog.Name, model);

    private async Task PublishAsync(CkCompiledModelRoot model, string sourceDir) =>
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name,
            model, new OriginFileResolver(sourceDir), isForced: false);

    private static string[] Codes(IEnumerable<CkCompileGateMessage> messages) =>
        messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    private static string Dump(IEnumerable<CkCompileGateMessage> messages) =>
        string.Join(Environment.NewLine, messages.Select(m => $"{m.Severity} {m.Code} {m.Text}"));

    /// <summary>Publishes 2.4.0 and the acknowledged Minor 2.5.0 through the catalog service (publish gate included).</summary>
    private async Task<CkCompiledModelRoot> PublishReleaseWithAcknowledgementAsync()
    {
        var baselineDir = WriteModel("2.4.0");
        await PublishAsync(await CompileToFileAndBackAsync(baselineDir), baselineDir);

        var releaseDir = WriteModel("2.5.0", "Hidden", acknowledge: Acknowledge((SecretKey, Reason)));
        var release = await CompileToFileAndBackAsync(releaseDir);
        var compileGate = await RunCompileGateAsync(release);
        Assert.False(compileGate.HasErrors, Dump(compileGate.Messages));
        await PublishAsync(release, releaseDir);
        return release;
    }

    [Fact]
    public async Task A_rebuild_of_the_published_minor_with_its_acknowledgements_is_green_in_both_gates()
    {
        await PublishReleaseWithAcknowledgementAsync();

        // The CI rebuild of the released commit: identical source, the version is now the published baseline.
        var rebuild = await CompileToFileAndBackAsync(WriteModel("2.5.0", "Hidden",
            acknowledge: Acknowledge((SecretKey, Reason))));

        var compileGate = await RunCompileGateAsync(rebuild);
        Assert.False(compileGate.HasErrors, Dump(compileGate.Messages));
        Assert.Contains(compileGate.Messages, m => m.Text.Contains("a rebuild of the published release"));
        Assert.DoesNotContain(compileGate.Messages, m => m.Severity == CkCompileGateSeverity.Warning);

        var publishGate = await RunPublishGateAsync(rebuild);
        Assert.Equal(CkPublishGateDecision.AlreadyPublished, publishGate.Decision);
        Assert.False(publishGate.HasErrors, Dump(publishGate.Messages));
    }

    [Fact]
    public async Task Removing_the_acknowledgements_after_the_publish_is_green_in_both_gates_with_one_warning_each()
    {
        await PublishReleaseWithAcknowledgementAsync();

        // What PR #258 of octo-identity-services did after the CK204 advice: same structure, entries removed.
        var withoutEntriesDir = WriteModel("2.5.0", "Hidden");
        var withoutEntries = await CompileToFileAndBackAsync(withoutEntriesDir);

        var compileGate = await RunCompileGateAsync(withoutEntries);
        Assert.False(compileGate.HasErrors, Dump(compileGate.Messages));
        var warning = Assert.Single(compileGate.Messages, m => m.Severity == CkCompileGateSeverity.Warning);
        Assert.Contains("differ from those of the published release 'GateModel-2.5.0'", warning.Text);

        var publishGate = await RunPublishGateAsync(withoutEntries);
        Assert.Equal(CkPublishGateDecision.AlreadyPublished, publishGate.Decision);
        Assert.Empty(Codes(publishGate.Messages)); // no OCTO-CK205, no OCTO-CK203 against 2.4.0
        Assert.Contains("acknowledgements",
            Assert.Single(publishGate.Messages, m => m.Severity == CkCompileGateSeverity.Warning).Text);

        // The publish is a no-op: the catalog keeps the release as published, entries included.
        await PublishAsync(withoutEntries, withoutEntriesDir);
        var stored = await _published.GetAsync(new CkModelId("GateModel", "2.5.0"), new OperationResult());
        Assert.Equal(SecretKey, Assert.Single(stored.Compatibility!.Acknowledge!).Change);
    }

    [Fact]
    public async Task A_structural_change_under_the_published_version_says_raise_the_version_not_remove_the_entry()
    {
        await PublishReleaseWithAcknowledgementAsync();

        // Enum value B removed (Major) without a version raise; the release's entry is still in the source.
        var changed = await CompileToFileAndBackAsync(WriteModel("2.5.0", "Hidden", "A",
            Acknowledge((SecretKey, Reason))));

        var compileGate = await RunCompileGateAsync(changed);
        Assert.Equal(["OCTO-CK200", "OCTO-CK204"], Codes(compileGate.Messages).OrderBy(c => c));
        var stale = compileGate.Messages.Single(m => m.Code == "OCTO-CK204").Text;
        Assert.Contains("already published under the declared version", stale);
        Assert.Contains("Raise the version in ckModel.yaml first", stale);
        Assert.DoesNotContain("remove the entry from compatibility.acknowledge", stale);

        // The publish gate refuses the same model with CK205 first; the next step is the same: raise the version.
        var publishGate = await RunPublishGateAsync(changed);
        Assert.Equal(CkPublishGateDecision.Refused, publishGate.Decision);
        Assert.Equal("OCTO-CK205", Codes(publishGate.Messages).First());
    }

    [Fact]
    public async Task The_next_release_still_judges_the_spent_entry_stale_with_the_remove_advice()
    {
        await PublishReleaseWithAcknowledgementAsync();

        var next = await CompileToFileAndBackAsync(WriteModel("2.6.0", "Hidden",
            acknowledge: Acknowledge((SecretKey, Reason))));

        var compileGate = await RunCompileGateAsync(next);
        Assert.Equal(["OCTO-CK204"], Codes(compileGate.Messages));
        Assert.Contains("remove the entry from compatibility.acknowledge",
            compileGate.Messages.Single(m => m.Code == "OCTO-CK204").Text);
        var publishGate = await RunPublishGateAsync(next);
        Assert.Equal(["OCTO-CK204"], Codes(publishGate.Messages));
    }

    [Fact]
    public async Task A_new_entry_added_to_the_published_version_is_not_accepted_as_a_change_of_the_release()
    {
        await PublishReleaseWithAcknowledgementAsync();

        // Same structure, an additional entry: the published release stays as it is, both gates say so as a warning.
        var extraDir = WriteModel("2.5.0", "Hidden",
            acknowledge: Acknowledge((SecretKey, Reason), ("EnumValue:State-1/B#Removed", "later")));
        var extra = await CompileToFileAndBackAsync(extraDir);

        var compileGate = await RunCompileGateAsync(extra);
        Assert.False(compileGate.HasErrors, Dump(compileGate.Messages));
        Assert.Single(compileGate.Messages, m => m.Severity == CkCompileGateSeverity.Warning);
        Assert.Equal(CkPublishGateDecision.AlreadyPublished, (await RunPublishGateAsync(extra)).Decision);

        await PublishAsync(extra, extraDir);
        var stored = await _published.GetAsync(new CkModelId("GateModel", "2.5.0"), new OperationResult());
        Assert.Single(stored.Compatibility!.Acknowledge!);
    }
}
