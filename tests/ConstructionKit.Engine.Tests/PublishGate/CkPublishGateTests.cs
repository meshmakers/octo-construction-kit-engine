using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using RowTestSupport = Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6386: gate 2 of the compatibility concept. Every test publishes through the real
///     <c>CatalogService.PublishAsync</c> (the path of <c>ckc Publish</c> and of <c>CkCompile</c>) or asks the gate
///     directly, against an in-memory catalog. No network, no shared catalog (operating rule O1).
/// </summary>
public sealed class CkPublishGateTests : IDisposable
{
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static string[] Codes(CkPublishGateResult r) => GateHarness.ErrorCodes(r);

    [Fact]
    public async Task Breaking_change_with_minor_bump_is_refused_with_CK200_and_nothing_is_written()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() =>
            _h.PublishAsync(GateModels.Create("2.5.0", enumValues: "A")));

        Assert.Contains("OCTO-CK200", exception.Message);
        Assert.Contains("MAJOR", exception.Message);
        Assert.Contains("3.0.0", exception.Message);
        Assert.Equal(0, _h.Stub.PublishCount);
        Assert.DoesNotContain(_h.Stub.ModelIds, id => id.Version == new CkVersion("2.5.0"));
        Assert.Contains(exception.OperationResult.Messages, m => m.MessageNumber == 200);
    }

    [Fact]
    public async Task The_same_change_with_a_major_bump_is_published()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));

        await _h.PublishAsync(GateModels.Create("3.0.0", enumValues: "A"));

        Assert.Equal(1, _h.Stub.PublishCount);
    }

    [Fact]
    public async Task One_CK200_per_uncovered_change()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0", enumValues: "A,B,C"));

        var result = await _h.EvaluateAsync(GateModels.Create("2.4.1", enumValues: "A"));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK200", "OCTO-CK200"], Codes(result));
    }

    [Fact]
    public async Task Additive_change_passes_and_the_verdict_line_names_model_version_baseline_and_catalog()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        var line = Assert.Single(result.Messages).Text;
        Assert.Contains("model 'GateModel'", line);
        Assert.Contains("declared 2.5.0", line);
        Assert.Contains($"baseline GateModel-2.4.0 ({StubCatalog.Name})", line);
        Assert.Contains("required level MINOR", line);
    }

    [Fact]
    public async Task A_downgrade_is_refused_with_CK201()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));

        var result = await _h.EvaluateAsync(GateModels.Create("2.3.9"));

        Assert.Equal(["OCTO-CK201"], Codes(result));
    }

    [Fact]
    public async Task A_maintenance_release_of_an_older_major_is_judged_against_its_own_major()
    {
        await _h.SeedAsync(GateModels.Create("3.4.0"));
        await _h.SeedAsync(GateModels.Create("4.0.0", enumValues: "A"));

        var result = await _h.EvaluateAsync(GateModels.Create("3.4.1"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Equal("GateModel-3.4.0", result.Verdict!.BaselineId.FullName);
    }

    [Fact]
    public async Task A_first_publication_passes()
    {
        var result = await _h.EvaluateAsync(GateModels.Create("1.0.0"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Contains("first publication", Assert.Single(result.Messages).Text);
    }

    [Fact]
    public async Task Entries_of_the_local_catalog_are_never_the_baseline()
    {
        // The local catalog holds a higher, breaking-incompatible version from an earlier local build.
        await _h.SeedAsync(GateModels.Create("2.9.0", enumValues: "A"), LocalFileSystemCatalog.Name);
        await _h.SeedAsync(GateModels.Create("2.4.0"));

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Equal("GateModel-2.4.0", result.Verdict!.BaselineId.FullName);
        Assert.False(result.Verdict.Resolution.IsLocal);
    }

    [Fact]
    public async Task A_model_only_the_local_catalog_knows_is_a_first_publication_for_the_target()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"), LocalFileSystemCatalog.Name);

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Null(result.Verdict);
    }

    [Fact]
    public async Task An_unreachable_target_catalog_is_CK202_never_a_first_publication()
    {
        _h.Stub.SourceUnreachable = true;

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0"));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK202"], Codes(result));
    }

    [Fact]
    public async Task A_disabled_target_catalog_is_CK202_never_a_first_publication()
    {
        _h.Stub.CanReadValue = false;

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0"));

        Assert.Equal(["OCTO-CK202"], Codes(result));
    }

    [Fact]
    public async Task An_unreachable_source_with_a_cached_baseline_still_judges_the_version()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));
        _h.Stub.SourceUnreachable = true;

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A"));

        Assert.Equal(["OCTO-CK200"], Codes(result));
        Assert.Contains(result.Messages, m => m.Text.Contains("may be stale"));
    }

    [Fact]
    public async Task The_local_file_system_catalog_is_not_gated()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"), LocalFileSystemCatalog.Name);

        await _h.PublishAsync(GateModels.Create("2.5.0", enumValues: "A"), LocalFileSystemCatalog.Name, force: true);
    }

    // ---- same-version rule -------------------------------------------------------------------------------------

    [Fact]
    public async Task Republishing_identical_content_is_a_no_op()
    {
        await _h.SeedAsync(GateModels.Create("2.5.0"));

        var result = await _h.EvaluateAsync(GateModels.Create("2.5.0"));
        await _h.PublishAsync(GateModels.Create("2.5.0"), force: true);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, result.Decision);
        Assert.Equal(0, _h.Stub.PublishCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Different_content_under_a_published_version_is_refused_with_CK205_also_with_replace(bool force)
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));
        await _h.SeedAsync(GateModels.Create("2.5.0"));

        var exception = await Assert.ThrowsAsync<CompilerException>(() =>
            _h.PublishAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"), force: force));

        Assert.Contains("OCTO-CK205", exception.Message);
        Assert.Equal(0, _h.Stub.PublishCount);
    }

    [Fact]
    public async Task On_the_lane_catalog_a_replace_works_and_is_judged_against_the_newest_version_below()
    {
        using var lane = new GateHarness(lane: true);
        await lane.SeedAsync(GateModels.Create("2.4.0"));
        await lane.SeedAsync(GateModels.Create("2.5.0"));

        // 2.5.0 is replaced with an additive change; the baseline is 2.4.0, not 2.5.0 itself.
        var replace = await lane.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"));
        Assert.Equal(CkPublishGateDecision.Proceed, replace.Decision);
        Assert.Equal("GateModel-2.4.0", replace.Verdict!.BaselineId.FullName);

        await lane.PublishAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"), force: true);
        Assert.Equal(1, lane.Stub.PublishCount);

        // After the replace the next publish gets the same baseline (no self-comparison).
        var next = await lane.EvaluateAsync(GateModels.Create("2.6.0", enumValues: "A,B,C,D"));
        Assert.Equal("GateModel-2.5.0", next.Verdict!.BaselineId.FullName);

        // The lane replace is judged by the verdict too: a breaking replace of 2.5.0 is refused.
        var breaking = await lane.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A"));
        Assert.Equal(["OCTO-CK200"], Codes(breaking));
    }

    [Fact]
    public async Task A_refused_publish_leaves_the_next_baseline_unchanged()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));
        await Assert.ThrowsAsync<CompilerException>(() => _h.PublishAsync(GateModels.Create("2.5.0", enumValues: "A")));

        var next = await _h.EvaluateAsync(GateModels.Create("2.5.0", enumValues: "A,B,C"));

        Assert.Equal("GateModel-2.4.0", next.Verdict!.BaselineId.FullName);
    }

    // ---- race --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_stale_baseline_never_decides_a_publish()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));
        _h.Stub.OnRefresh = n =>
        {
            if (n == 2)
            {
                _h.Stub.Seed(GateModels.Create("2.5.0", enumValues: "A,B,C"));
            }
        };

        // Valid against 2.4.0 (unchanged content, patch) but behind 2.5.0 once that has landed: the verdict is re-run.
        var result = await _h.EvaluateAsync(GateModels.Create("2.4.5"));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal("GateModel-2.5.0", result.Verdict!.BaselineId.FullName);
        Assert.Equal(2, _h.Stub.RefreshCount); // evaluate, then the confirming read that saw 2.5.0
    }

    [Fact]
    public async Task A_catalog_that_keeps_changing_fails_the_publish()
    {
        await _h.SeedAsync(GateModels.Create("2.4.0"));
        var minor = 5;
        _h.Stub.OnRefresh = n =>
        {
            if (n >= 2)
            {
                _h.Stub.Seed(GateModels.Create($"2.{minor++}.0", enumValues: "A,B,C"));
            }
        };

        var result = await _h.EvaluateAsync(GateModels.Create("2.99.0", enumValues: "A,B,C,D"));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK202"], Codes(result));
    }

    // ---- case-insensitive baseline lookup ----------------------------------------------------------------------

    [Theory]
    [InlineData("System.Bot", "system.bot")]
    [InlineData("system.bot", "System.Bot")]
    public async Task The_baseline_of_a_case_only_name_variant_is_found_and_never_a_first_publication(
        string published, string publishing)
    {
        await _h.SeedAsync(GateModels.Create("3.1.0", name: published));

        var result = await _h.EvaluateAsync(GateModels.Create("3.2.0", name: publishing, withSerial: false));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal($"{published}-3.1.0", result.Verdict!.BaselineId.FullName);
        Assert.Contains("OCTO-CK200", Codes(result));
    }

    [Fact]
    public async Task The_same_lookup_serves_the_compile_gate_and_ValidateVersion()
    {
        await _h.SeedAsync(GateModels.Create("3.1.0", name: "System.Bot"));

        var resolver = _h.Get<ICkBaselineResolver>();
        var resolution = await resolver.ResolveAsync("system.bot", new CkVersion("3.2.0"), CkBaselineSource.Remote);

        Assert.False(resolution.IsFirstPublication);
        Assert.Equal("System.Bot-3.1.0", resolution.Baseline!.FullName);
    }

    // ---- shared classifier --------------------------------------------------------------------------------------

    public static TheoryData<string, Action<CkCompiledModelRoot>> ClassifierCorpus => new()
    {
        { "default-deny: roles emptied", m => RowTestSupport.Method(m, "type").Authorization!.Roles = [] },
        { "role names compare case-insensitively", m => RowTestSupport.Method(m, "type").Authorization!.Roles = ["usermanagement"] },
        { "method added", m => RowTestSupport.Methods(m, "type").Add(new() { MethodId = "Calibrate-1" }) },
    };

    [Theory]
    [MemberData(nameof(ClassifierCorpus))]
    public async Task The_publish_path_gives_the_same_required_level_as_the_classifier(string _,
        Action<CkCompiledModelRoot> change)
    {
        var baseline = RowTestSupport.Model();
        baseline.ModelId = new CkModelId(RowTestSupport.M, "1.0.0");
        await _h.SeedAsync(baseline);
        var current = RowTestSupport.Model();
        current.ModelId = new CkModelId(RowTestSupport.M, "9.0.0");
        change(current);
        var expected = RowTestSupport.Level(RowTestSupport.Model(), current);

        var result = await _h.EvaluateAsync(current);

        Assert.Equal(expected, result.Verdict!.RequiredLevel);
    }
}
