using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6386 against the REAL <see cref="GitHubCatalog" /> over an in-memory GitHub repository: both catalog roots
///     (<c>ck-models/v2</c> for ckLanguage 1, <c>ck-models/v3</c> for ckLanguage 2), the case-sensitive index cache and
///     the written files. No network, no shared catalog (operating rule O1).
/// </summary>
public sealed class CkPublishGateRepositoryTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Task PublishAsync(CkCompiledModelRoot model) => _h.PublishAsync(model, Catalog);

    private Task<CkPublishGateResult> EvaluateAsync(CkCompiledModelRoot model) => _h.EvaluateAsync(model, Catalog);

    private static CkCompiledModelRoot V1(string version, string enumValues = "A,B", string? name = null,
        bool withSerial = true) => GateModels.Create(version, enumValues, null, name, withSerial);

    private static CkCompiledModelRoot V2(string version, string enumValues = "A,B", string? name = null,
        bool withSerial = true) => GateModels.Create(version, enumValues, 2, name, withSerial);

    private bool HasFile(string path) => _h.Repository.Files.ContainsKey(path);

    [Fact]
    public async Task A_refused_publish_leaves_every_file_of_the_repository_byte_identical()
    {
        await PublishAsync(V2("2.4.0"));
        var before = _h.Repository.Snapshot();

        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => PublishAsync(V2("2.5.0", "A")));

        Assert.Contains("OCTO-CK200", exception.AllText());
        Assert.Equal(before, _h.Repository.Snapshot());

        await PublishAsync(V2("3.0.0", "A"));
        Assert.NotEqual(before, _h.Repository.Snapshot());
        Assert.True(HasFile("ck-models/v3/g/GateModel/3/ck-gatemodel-3.0.0.json"));
    }

    [Fact]
    public async Task The_first_ckLanguage_2_release_is_diffed_against_the_newest_2x_under_the_v2_root()
    {
        await PublishAsync(V1("2.3.0"));
        await PublishAsync(V1("2.4.0"));
        Assert.True(HasFile("ck-models/v2/g/GateModel/2/ck-gatemodel-2.4.0.json"));

        var result = await EvaluateAsync(V2("3.0.0", "A"));

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Equal("GateModel-2.4.0", result.Verdict!.BaselineId.FullName);
        Assert.True(result.Verdict.Resolution.IsFromLowerMajor);
    }

    [Fact]
    public async Task A_ckLanguage_2_release_of_the_old_major_is_still_judged_against_the_v2_root()
    {
        await PublishAsync(V1("2.4.0"));

        var result = await EvaluateAsync(V2("2.5.0", "A"));

        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(result));
        Assert.Equal("GateModel-2.4.0", result.Verdict!.BaselineId.FullName);
    }

    [Fact]
    public async Task A_later_release_is_diffed_against_the_previous_one_under_the_v3_root()
    {
        await PublishAsync(V1("2.4.0"));
        await PublishAsync(V2("3.0.0", "A"));
        Assert.True(HasFile("ck-models/v3/g/GateModel/3/ck-gatemodel-3.0.0.json"));

        var additive = await EvaluateAsync(V2("3.1.0", "A,B"));
        var breaking = await EvaluateAsync(V2("3.1.0", "A", withSerial: false));

        Assert.Equal(CkPublishGateDecision.Proceed, additive.Decision);
        Assert.Equal("GateModel-3.0.0", additive.Verdict!.BaselineId.FullName);
        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(breaking));
        Assert.Equal("GateModel-3.0.0", breaking.Verdict!.BaselineId.FullName);
    }

    [Fact]
    public async Task The_same_version_in_both_roots_is_refused()
    {
        await PublishAsync(V1("3.0.0"));

        // Same version, now ckLanguage 2: it would land in the other root with different content.
        var result = await EvaluateAsync(V2("3.0.0"));
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(V2("3.0.0"), Catalog, true));

        Assert.Equal(["OCTO-CK205"], GateHarness.ErrorCodes(result));
        Assert.Contains("OCTO-CK205", exception.AllText());
        Assert.False(HasFile("ck-models/v3/g/GateModel/3/ck-gatemodel-3.0.0.json"));
    }

    [Fact]
    public async Task Republishing_identical_content_writes_nothing()
    {
        await PublishAsync(V2("2.5.0"));
        var before = _h.Repository.Snapshot();

        await _h.PublishAsync(V2("2.5.0"), Catalog, force: true);

        Assert.Equal(before, _h.Repository.Snapshot());
    }

    [Theory]
    [InlineData(false, "System.Bot", "system.bot")]
    [InlineData(false, "system.bot", "System.Bot")]
    [InlineData(true, "System.Bot", "system.bot")]
    [InlineData(true, "system.bot", "System.Bot")]
    public async Task A_case_only_name_variant_finds_its_baseline_in_either_root(bool v3Root, string published,
        string publishing)
    {
        await PublishAsync(v3Root ? V2("3.1.0", name: published) : V1("3.1.0", name: published));

        var result = await EvaluateAsync(
            v3Root ? V2("3.2.0", name: publishing, withSerial: false) : V1("3.2.0", name: publishing, withSerial: false));

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal($"{published}-3.1.0", result.Verdict!.BaselineId.FullName);
        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(result));
        Assert.False(result.Verdict.Resolution.IsFirstPublication);
    }

    [Fact]
    public async Task The_dev_lane_catalog_replaces_a_version_with_the_judged_content()
    {
        using var lane = new GateHarness(lane: true);
        await lane.PublishAsync(V2("2.4.0"), Catalog);
        await lane.PublishAsync(V2("2.5.0"), Catalog);

        await lane.PublishAsync(V2("2.5.0", "A,B,C"), Catalog, force: true);

        var payload = lane.Repository.Files["ck-models/v3/g/GateModel/2/ck-gatemodel-2.5.0.json"];
        Assert.Contains("\"C\"", payload);
        var breaking = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            lane.PublishAsync(V2("2.5.0", "A"), Catalog, force: true));
        Assert.Contains("OCTO-CK200", breaking.AllText());
    }

    [Fact]
    public async Task The_published_model_files_are_byte_identical_to_a_publish_without_the_gate()
    {
        // A v1 release history with additive, maintenance and breaking releases, replayed through the gate.
        CkCompiledModelRoot[] history =
        [
            V1("1.0.0"), V1("1.1.0", "A,B,C"), V1("1.1.1", "A,B,C"), V1("1.2.0", "A,B,C,D"), V1("2.0.0", "A,B"),
            V1("2.1.0", "A,B,E")
        ];
        foreach (var model in history)
        {
            await PublishAsync(model);
        }

        using var direct = new GateHarness();
        var serializer = direct.Get<Contracts.Serialization.ICkJsonSerializer>();
        var catalog = new RepositoryBackedCatalog(serializer, direct.Repository, Path.Combine(direct.Root, "cache2"));
        Directory.CreateDirectory(Path.Combine(direct.Root, "cache2"));
        foreach (var model in history)
        {
            await catalog.PublishAsync(model, false); // the catalog without the gate
        }

        string[] ModelFiles(InMemoryGitHubRepository r) => r.Files.Where(f => f.Key.Contains("/ck-gatemodel-"))
            .OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}\n{f.Value}").ToArray();

        Assert.Equal(history.Length, ModelFiles(_h.Repository).Length);
        Assert.Equal(ModelFiles(direct.Repository), ModelFiles(_h.Repository));
    }
}
