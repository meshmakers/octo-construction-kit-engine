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
///     AB#6295: <c>compatibility.acknowledge</c> in <c>ckModel.yaml</c> against the compile gate. A record attribute
///     that is security-sensitive in both versions and tightened from ReadOnly to Hidden is Minor and needs an
///     acknowledgement; a unique index on a stable base is Major and needs one.
/// </summary>
public sealed class CkCompileGateAcknowledgeTests : IDisposable
{
    private const string SecretKey = "RecordAttribute:Login-1/Secret#Modified:access";

    private readonly InMemoryPublishedCatalog _published = new();
    private readonly CkCompileFixture _fixture;
    private int _outputCounter;

    public CkCompileGateAcknowledgeTests()
    {
        _fixture = new CkCompileFixture(s => s.AddSingleton<ICatalog>(_published));
    }

    public void Dispose() => _fixture.Dispose();

    private static string Acknowledge(params (string Change, string Reason)[] entries) =>
        entries.Length == 0
            ? ""
            : "compatibility:\n  acknowledge:\n" + string.Concat(entries.Select(e =>
                $"    - change: \"{e.Change}\"\n      reason: \"{e.Reason}\"\n"));

    /// <summary>A record with a security-sensitive attribute whose access and acknowledgements the tests vary.</summary>
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

    private async Task PublishBaselineAsync(string version = "2.4.0")
    {
        var dir = WriteModel(version);
        var compiled = await _fixture.CompileAsync(dir);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name,
            compiled, new OriginFileResolver(dir), isForced: true);
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

    private async Task<CkCompileGateResult> RunGateAsync(string sourceDir) =>
        await _fixture.Services.GetRequiredService<CkCompileGate>()
            .RunAsync(await CompileToFileAndBackAsync(sourceDir), CkBaselineSource.Remote);

    private static string[] Codes(CkCompileGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    [Fact]
    public async Task A_tightened_security_sensitive_attribute_without_acknowledgement_fails_with_CK203_and_prints_the_key()
    {
        await PublishBaselineAsync();

        var result = await RunGateAsync(WriteModel("2.5.0", "Hidden"));

        Assert.Equal(["OCTO-CK203"], Codes(result));
        var text = result.Messages.Single(m => m.Code == "OCTO-CK203").Text;
        Assert.Contains($"\"{SecretKey}\"", text);
        Assert.Contains("compatibility:", text);
        Assert.Contains("acknowledge:", text);
        Assert.Contains("reason:", text);
    }

    [Fact]
    public async Task A_matching_entry_passes_and_is_listed_in_the_verdict_and_the_changelog()
    {
        await PublishBaselineAsync();
        var dir = WriteModel("2.5.0", "Hidden",
            acknowledge: Acknowledge((SecretKey, "Close accepted risk R13: secret readable via GraphQL")));

        var result = await RunGateAsync(dir);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Messages,
            m => m.Text.Contains("Acknowledged change:") && m.Text.Contains("R13"));
        var acknowledged = Assert.Single(result.Verdict!.Acknowledgement.Acknowledged);
        Assert.Equal(SecretKey, acknowledged.Key);

        var changelog = new CkChangelogGenerator().Generate(null, new CkVersion(2, 5, 0), new DateTime(2026, 10, 10),
            result.Verdict.RequiredLevel, result.Verdict.ClassifiedChanges, null,
            result.Verdict.Acknowledgement.Acknowledged);
        Assert.Contains("### Acknowledged changes", changelog);
        Assert.Contains("acknowledged: Close accepted risk R13", changelog);
    }

    [Fact]
    public async Task An_entry_that_matches_no_change_is_stale_and_fails_with_CK204()
    {
        await PublishBaselineAsync();

        var result = await RunGateAsync(WriteModel("2.5.0", "ReadOnly",
            acknowledge: Acknowledge((SecretKey, "left over from the last release"))));

        Assert.Equal(["OCTO-CK204"], Codes(result));
        Assert.Contains("matches no change in this release",
            result.Messages.Single(m => m.Code == "OCTO-CK204").Text);
    }

    [Fact]
    public async Task An_acknowledgement_does_not_carry_over_to_the_next_release()
    {
        await PublishBaselineAsync();
        var acknowledge = Acknowledge((SecretKey, "accepted"));
        var release250 = WriteModel("2.5.0", "Hidden", acknowledge: acknowledge);
        Assert.False((await RunGateAsync(release250)).HasErrors);
        var compiled = await _fixture.CompileAsync(release250);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name,
            compiled, new OriginFileResolver(release250), isForced: true);

        var result = await RunGateAsync(WriteModel("2.6.0", "Hidden", acknowledge: acknowledge));

        Assert.Equal(["OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task An_entry_for_an_ordinary_breaking_change_does_not_waive_CK200()
    {
        await PublishBaselineAsync();
        var key = "EnumValue:State-1/B#Removed";

        var result = await RunGateAsync(WriteModel("2.5.0", enumValues: "A", acknowledge: Acknowledge((key, "please"))));

        Assert.Contains("OCTO-CK200", Codes(result));
        var stale = result.Messages.Single(m => m.Code == "OCTO-CK204").Text;
        Assert.Contains("needs no acknowledgement", stale);
        Assert.DoesNotContain("OCTO-CK203", Codes(result));
    }

    [Fact]
    public async Task An_acknowledgement_never_lowers_the_required_level()
    {
        await PublishBaselineAsync();

        // Patch bump over a Minor change: the entry is matched (no CK203/CK204) but the version is still too low.
        var result = await RunGateAsync(WriteModel("2.4.1", "Hidden",
            acknowledge: Acknowledge((SecretKey, "accepted"))));

        Assert.Equal(["OCTO-CK200"], Codes(result));
        Assert.Single(result.Verdict!.Acknowledgement.Acknowledged);
    }

    [Fact]
    public async Task The_acknowledgements_are_carried_into_the_compiled_model_json_and_omitted_when_empty()
    {
        var with = _fixture.WriteSource("with", "GateModel-1.0.0", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n"
        }, 2, Acknowledge(("TypeIndex:X/index#Added:y", "z")));
        var without = _fixture.WriteSource("without", "GateModel-1.0.0", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n"
        }, 2);
        var serializer = _fixture.Services.GetRequiredService<ICkSerializer>();

        var compiledWith = await _fixture.CompileAsync(with);
        var compiledWithout = await _fixture.CompileAsync(without);

        Assert.Equal("TypeIndex:X/index#Added:y", Assert.Single(compiledWith.Compatibility!.Acknowledge!).Change);
        Assert.Null(compiledWithout.Compatibility);
        var textWith = await ToTextAsync(serializer, compiledWith);
        var textWithout = await ToTextAsync(serializer, compiledWithout);
        Assert.Contains("compatibility", textWith);
        Assert.DoesNotContain("compatibility", textWithout);
    }

    private static async Task<string> ToTextAsync(ICkSerializer serializer, CkCompiledModelRoot model)
    {
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            await serializer.SerializeAsync(writer, model);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Theory]
    [InlineData("\"\"", "reason")]
    [InlineData("\"   \"", "reason")]
    public async Task An_empty_reason_is_rejected(string reason, string expectedFragment)
    {
        var dir = WriteModel("1.0.0");
        File.AppendAllText(Path.Combine(dir, "ckModel.yaml"),
            $"compatibility:\n  acknowledge:\n    - change: \"{SecretKey}\"\n      reason: {reason}\n");

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dir, operationResult));

        Assert.Contains(operationResult.Messages,
            m => m.MessageLevel >= Contracts.Messages.MessageLevel.Error &&
                 m.MessageText.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("RecordAttribute:*#Modified:access")]
    [InlineData("RecordAttribute:Login-1/Secret#Modified:acces?")]
    public async Task A_wildcard_key_is_rejected(string key)
    {
        var dir = WriteModel("1.0.0");
        File.AppendAllText(Path.Combine(dir, "ckModel.yaml"),
            $"compatibility:\n  acknowledge:\n    - change: \"{key}\"\n      reason: \"because\"\n");

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dir, operationResult));

        Assert.Contains(operationResult.Messages, m => m.MessageLevel >= Contracts.Messages.MessageLevel.Error);
    }

    [Fact]
    public async Task A_duplicate_entry_is_rejected_with_message_130()
    {
        var dir = WriteModel("1.0.0", acknowledge: Acknowledge((SecretKey, "one"), (SecretKey, "two")));

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dir, operationResult));

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 130 && m.MessageText.Contains("more than once"));
    }

    [Fact]
    public async Task Acknowledgements_in_a_ckLanguage_1_model_are_rejected_with_message_90()
    {
        var dir = _fixture.WriteSource("v1", "V1Model-1.0.0", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n"
        }, null, Acknowledge(("TypeIndex:X/index#Added:y", "z")));

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dir, operationResult));

        Assert.Contains(operationResult.Messages,
            m => m.MessageNumber == 90 && m.MessageText.Contains("compatibility.acknowledge"));
    }

    [Fact]
    public async Task On_a_first_publication_every_entry_is_stale()
    {
        var result = await RunGateAsync(WriteModel("1.0.0", acknowledge: Acknowledge((SecretKey, "no baseline"))));

        Assert.Equal(["OCTO-CK204"], Codes(result));
    }

    [Fact]
    public async Task The_key_is_free_of_model_version_numbers()
    {
        await PublishBaselineAsync();
        var minor = await RunGateAsync(WriteModel("2.5.0", "Hidden"));

        // The same change one release later: only the version in modelId differs, the key is identical.
        var again = await RunGateAsync(WriteModel("2.9.0", "Hidden"));

        Assert.Equal(minor.Verdict!.Acknowledgement.Unacknowledged.Single().Key,
            again.Verdict!.Acknowledgement.Unacknowledged.Single().Key);
    }

    /// <summary>A public, non-final type with derivable: Any on System/Entity: a stable base (row B4).</summary>
    private string WriteStableBaseModel(string version, bool uniqueIndex, string? acknowledge = null) =>
        _fixture.WriteSource("base", $"BaseModel-{version}", ["System-[2.5,3.0)"], new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = "attributes:\n  - id: Serial\n    valueType: String\n",
            ["types/machine.yaml"] = "types:\n  - typeId: Machine\n    derivedFromCkTypeId: ${System}/Entity\n" +
                                     "    derivable: Any\n    attributes:\n      - id: ${this}/Serial\n" +
                                     "        name: Serial\n        isOptional: true\n" +
                                     (uniqueIndex
                                         ? "    indexes:\n      - indexType: UniqueNotDeleted\n        fields:\n" +
                                           "          - attributePaths:\n              - Serial\n"
                                         : "")
        }, 2, acknowledge);

    [Fact]
    public async Task A_unique_index_on_a_stable_base_needs_a_major_bump_and_an_acknowledgement()
    {
        var systemDir = _fixture.WriteSystemModel("2.5.0");
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(LocalFileSystemCatalog.Name,
            await _fixture.CompileAsync(systemDir), new OriginFileResolver(systemDir), isForced: true);
        var baselineDir = WriteStableBaseModel("1.0.0", uniqueIndex: false);
        await _fixture.Services.GetRequiredService<ICatalogService>().PublishAsync(InMemoryPublishedCatalog.Name,
            await _fixture.CompileAsync(baselineDir), new OriginFileResolver(baselineDir), isForced: true);

        // Without acknowledgement: CK203 and CK200 (Major over a Minor bump); the key is printed.
        var unacknowledged = await RunGateAsync(WriteStableBaseModel("1.1.0", uniqueIndex: true));
        Assert.Equal(["OCTO-CK200", "OCTO-CK203"], Codes(unacknowledged).OrderBy(c => c));
        var key = unacknowledged.Verdict!.Acknowledgement.Unacknowledged.Single().Key;
        Assert.StartsWith("TypeIndex:Machine-1/index#Added:", key);
        Assert.Contains($"\"{key}\"", unacknowledged.Messages.Single(m => m.Code == "OCTO-CK203").Text);

        // Acknowledged but only a minor bump: the acknowledgement does not waive the major bump.
        var acknowledge = Acknowledge((key, "existing data was cleaned up in R14"));
        var minor = await RunGateAsync(WriteStableBaseModel("1.1.0", uniqueIndex: true, acknowledge));
        Assert.Equal(["OCTO-CK200"], Codes(minor));

        // Acknowledged with the major bump: passes.
        var major = await RunGateAsync(WriteStableBaseModel("2.0.0", uniqueIndex: true, acknowledge));
        Assert.False(major.HasErrors, string.Join("; ", major.Messages.Select(m => m.Text)));
    }

    [Theory]
    [InlineData("ok\\n\\n## [2.4.0] - 2020-01-01")]
    [InlineData("##vso[task.complete result=Succeeded;]")]
    public async Task A_reason_must_be_a_single_line_of_plain_text(string reason)
    {
        var dir = WriteModel("1.0.0", acknowledge: Acknowledge((SecretKey, reason)));

        var operationResult = new OperationResult();
        await Assert.ThrowsAnyAsync<Exception>(() => _fixture.Services.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(dir, operationResult));

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 130 && m.MessageText.Contains("single line"));
    }
}
