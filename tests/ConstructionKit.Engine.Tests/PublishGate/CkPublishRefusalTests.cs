using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6389: one coherent refusal. All reasons of one publish are reported together, ordered, without duplicates,
///     each as a self-contained line that starts with the gate name. The O1 guard (pipeline) never reaches the engine,
///     so no engine text mentions it; the engine result does not depend on the O1 state. Everything runs against an
///     in-memory GitHub repository (operating rule O1: no shared catalog).
/// </summary>
public sealed class CkPublishRefusalTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static CkCompiledModelRoot V1(string version, string enumValues = "A,B") =>
        GateModels.Create(version, enumValues, null);

    private static CkCompiledModelRoot V2(string version, string enumValues = "A,B") =>
        GateModels.Create(version, enumValues);

    private static string[] ErrorLines(CkPublishRefusedException exception) =>
        exception.OperationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Error)
            .Select(m => m.MessageText).ToArray();

    [Fact]
    public async Task A_replaced_version_that_is_also_breaking_reports_both_reasons_in_one_refusal()
    {
        await _h.PublishAsync(V2("2.4.0"), Catalog);
        await _h.PublishAsync(V2("2.5.0"), Catalog);

        // 2.5.0 is published with different content AND the new content is breaking over 2.4.0 (a minor bump).
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _h.PublishAsync(V2("2.5.0", "A"), Catalog, true));

        var lines = ErrorLines(exception);
        Assert.Equal(2, lines.Length);
        Assert.Contains("OCTO-CK205", lines[0]); // the version number is unusable: first
        Assert.Contains("OCTO-CK200", lines[1]);
        Assert.Contains("2 reason(s) (OCTO-CK205, OCTO-CK200)", exception.Message);
    }

    [Fact]
    public async Task A_burned_version_number_is_reported_by_the_gate_together_with_the_other_reasons()
    {
        await _h.PublishAsync(V1("2.4.0"), Catalog);
        await _h.PublishAsync(V1("2.5.0"), Catalog);
        await _h.CatalogService.UnpublishAsync(Catalog, new CkModelId(GateModels.Name, "2.5.0"), "published too early");

        // The number 2.5.0 is burned, and the content is breaking over 2.4.0 under a minor bump.
        var result = await _h.EvaluateAsync(V1("2.5.0", "A"), Catalog);
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _h.PublishAsync(V1("2.5.0", "A"), Catalog));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK207", "OCTO-CK200"], GateHarness.ErrorCodes(result));
        var lines = ErrorLines(exception);
        Assert.Contains("burned", lines[0]);
        Assert.Contains("published too early", lines[0]);
        Assert.Contains("next free version number", lines[0]);
    }

    [Fact]
    public async Task A_burned_version_number_is_refused_on_its_own_with_a_next_step()
    {
        await _h.PublishAsync(V1("2.4.0"), Catalog);
        await _h.PublishAsync(V1("2.5.0", "A,B,C"), Catalog);
        await _h.CatalogService.UnpublishAsync(Catalog, new CkModelId(GateModels.Name, "2.5.0"), null);

        var result = await _h.EvaluateAsync(V1("2.5.0", "A,B,C"), Catalog);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK207"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task Every_reason_line_names_the_gate_the_model_the_catalog_and_the_next_step_and_never_mentions_O1()
    {
        await _h.PublishAsync(V2("2.4.0"), Catalog);
        await _h.PublishAsync(V2("2.5.0"), Catalog);

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _h.PublishAsync(V2("2.5.0", "A"), Catalog, true));

        var all = exception.AllText();
        foreach (var line in ErrorLines(exception))
        {
            Assert.StartsWith("[compat gate] OCTO-CK2", line);
            Assert.Contains("model 'GateModel-2.5.0'", line);
            Assert.Contains($"catalog '{Catalog}'", line);
            Assert.Contains("Next step: ", line);
        }

        Assert.StartsWith("Construction kit model 'GateModel-2.5.0' was NOT published to catalog", exception.Message);
        Assert.DoesNotContain("O1", all.Replace("OCTO", ""));
        Assert.DoesNotContain("OctoCkO1Lifted", all);
    }

    [Fact]
    public async Task The_same_publish_gives_the_same_lines_whatever_the_caller()
    {
        await _h.PublishAsync(V2("2.4.0"), Catalog);

        // ckc Publish and the MSBuild publish of CkCompile both call ICatalogService.PublishAsync with an
        // OperationResult and print its messages; the lines are therefore equal for equal input.
        var first = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(V2("2.5.0", "A"), Catalog));
        var second = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(V2("2.5.0", "A"), Catalog));

        Assert.Equal(ErrorLines(first), ErrorLines(second));
        Assert.Equal(first.Message, second.Message);
    }

    [Fact]
    public async Task The_dev_lane_catalog_refuses_a_breaking_ckLanguage_2_version_and_publishes_a_compatible_one()
    {
        using var lane = new GateHarness(lane: true);
        await lane.PublishAsync(V2("2.4.0"), Catalog);

        // The engine decides from the model and the target catalog only; the O1 state of the pipeline is not an input.
        var breaking = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            lane.PublishAsync(V2("2.5.0", "A"), Catalog));
        await lane.PublishAsync(V2("2.5.0", "A,B,C"), Catalog);

        Assert.Contains("OCTO-CK200", breaking.AllText());
        Assert.True(lane.Repository.Files.ContainsKey("ck-models/v3/g/GateModel/2/ck-gatemodel-2.5.0.json"));
    }

    [Fact]
    public async Task A_v1_model_is_published_exactly_as_before()
    {
        await _h.PublishAsync(V1("1.0.0"), Catalog);
        await _h.PublishAsync(V1("1.1.0", "A,B,C"), Catalog);

        Assert.True(_h.Repository.Files.ContainsKey("ck-models/v2/g/GateModel/1/ck-gatemodel-1.1.0.json"));
    }

    [Fact]
    public void Normalize_removes_repeated_findings_and_orders_the_errors_after_the_information()
    {
        CkCompileGateMessage[] messages =
        [
            new(CkCompileGateSeverity.Error, "OCTO-CK203", "needs acknowledgement"),
            new(CkCompileGateSeverity.Info, null, "info"),
            new(CkCompileGateSeverity.Error, "OCTO-CK200", "level a"),
            new(CkCompileGateSeverity.Error, "OCTO-CK200", "level b"),
            new(CkCompileGateSeverity.Error, "OCTO-CK200", "level a"),
            new(CkCompileGateSeverity.Warning, null, "warning"),
            new(CkCompileGateSeverity.Error, "OCTO-CK205", "immutable"),
            new(CkCompileGateSeverity.Error, "OCTO-CK204", "stale"),
            new(CkCompileGateSeverity.Error, "OCTO-CK206", "invalid entry")
        ];

        var normalized = CkPublishRefusal.Normalize(messages);

        Assert.Equal(["info", "warning"], normalized.Take(2).Select(m => m.Text));
        Assert.Equal(["OCTO-CK205", "OCTO-CK200", "OCTO-CK200", "OCTO-CK206", "OCTO-CK203", "OCTO-CK204"],
            normalized.Skip(2).Select(m => m.Code));
        Assert.Equal(["level a", "level b"], normalized.Where(m => m.Code == "OCTO-CK200").Select(m => m.Text));
    }

    [Theory]
    [InlineData("OCTO-CK200")]
    [InlineData("OCTO-CK201")]
    [InlineData("OCTO-CK202")]
    [InlineData("OCTO-CK203")]
    [InlineData("OCTO-CK204")]
    [InlineData("OCTO-CK205")]
    [InlineData("OCTO-CK206")]
    [InlineData("OCTO-CK207")]
    public void Every_code_of_the_gate_has_a_next_step(string code) =>
        Assert.False(string.IsNullOrWhiteSpace(CkPublishRefusal.GetNextStep(code)));
}
