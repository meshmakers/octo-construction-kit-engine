using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6584: two publishes of one model that run at the same time are both judged against the baseline both of them
///     read. The real <see cref="GitHubCatalog" /> over an in-memory repository, a second harness on the same
///     repository as the second process, and the other publish injected exactly between the baseline read and the
///     write of the first. Whichever publish lands second must be judged against the first.
/// </summary>
public sealed class CkPublishRaceTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private const string AddsDelta = "A,B,C,D";
    private const string Baseline = "A,B,C";

    private readonly GateHarness _a = new();
    private readonly GateHarness _b;

    public CkPublishRaceTests()
    {
        _b = new GateHarness(repository: _a.Repository);
    }

    public void Dispose()
    {
        _b.Dispose();
        _a.Dispose();
    }

    private static CkCompiledModelRoot V(string version, string values) => GateModels.Create(version, values);

    private static string FilePath(string version, string name = GateModels.Name) =>
        $"ck-models/v3/{char.ToLowerInvariant(name[0])}/{name}/1/ck-{name.ToLowerInvariant()}-{version}.json";

    private bool HasFile(string version, string name = GateModels.Name) =>
        _a.Repository.Files.ContainsKey(FilePath(version, name));

    private async Task SeedBaselineAsync()
    {
        await _a.PublishAsync(V("1.0.0", Baseline), Catalog);
        _a.Repository.ThawPages();
    }

    /// <summary>The other process publishes <paramref name="other" /> when <paramref name="mine" /> reaches its first write.</summary>
    private void OtherProcessWinsTheRaceOf(string mine, CkCompiledModelRoot other)
    {
        _a.Repository.BeforeWrite = async path =>
        {
            if (path.Contains($"-{mine}.json", StringComparison.Ordinal))
            {
                await _b.PublishAsync(other, Catalog);
            }
        };
    }

    [Fact]
    public async Task The_removal_that_lands_second_is_judged_against_the_addition_and_refused()
    {
        await SeedBaselineAsync();
        // The bug: 1.1.1 (the content of 1.0.0, a removal of Delta relative to 1.1.0) was judged against 1.0.0 only.
        OtherProcessWinsTheRaceOf("1.1.1", V("1.1.0", AddsDelta));

        var refused = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _a.PublishAsync(V("1.1.1", Baseline), Catalog));

        Assert.Contains("OCTO-CK200", refused.AllText());
        Assert.Contains("GateModel-1.1.0", refused.AllText());
        Assert.True(HasFile("1.0.0"));
        Assert.True(HasFile("1.1.0"));
        Assert.False(HasFile("1.1.1"), "the aborted publish must leave no model file behind");
        Assert.DoesNotContain("1.1.1", _a.Repository.Files["ck-models/v3/g/GateModel/1/catalog.json"]);
        Assert.False(_a.Repository.Files.ContainsKey("ck-models/v3/g/GateModel/withdrawn.json"),
            "the conditional write refused it before it was visible: no number is burned");
    }

    [Fact]
    public async Task The_addition_that_lands_second_is_judged_against_the_patch_and_refused_as_lower_than_the_baseline()
    {
        await SeedBaselineAsync();
        OtherProcessWinsTheRaceOf("1.1.0", V("1.1.1", Baseline));

        var refused = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _a.PublishAsync(V("1.1.0", AddsDelta), Catalog));

        Assert.Contains("OCTO-CK201", refused.AllText());
        Assert.True(HasFile("1.1.1"));
        Assert.False(HasFile("1.1.0"));
        Assert.False(_a.Repository.Files.ContainsKey("ck-models/v3/g/GateModel/withdrawn.json"));
    }

    [Fact]
    public async Task A_compatible_publish_that_lost_the_race_is_judged_again_and_lands()
    {
        await SeedBaselineAsync();
        OtherProcessWinsTheRaceOf("1.2.0", V("1.1.0", AddsDelta));

        await _a.PublishAsync(V("1.2.0", "A,B,C,D,E"), Catalog);

        Assert.True(HasFile("1.1.0"));
        Assert.True(HasFile("1.2.0"));
        var index = _a.Repository.Files["ck-models/v3/g/GateModel/1/catalog.json"];
        Assert.Contains("1.1.0", index);
        Assert.Contains("1.2.0", index);
    }

    [Fact]
    public async Task A_catalog_that_keeps_changing_ends_with_CK202_and_leaves_nothing_behind()
    {
        await SeedBaselineAsync();
        var next = 1;
        _a.Repository.BeforeWrite = null;
        void Arm()
        {
            _a.Repository.BeforeWrite = async path =>
            {
                // The creation of the model file of 1.9.0 (not its removal after the aborted attempt).
                if (path.Contains("-1.9.0.json", StringComparison.Ordinal) && !_a.Repository.Files.ContainsKey(path))
                {
                    await _b.PublishAsync(V($"1.0.{next++}", Baseline), Catalog);
                }

                Arm();
            };
        }

        Arm();

        var refused = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _a.PublishAsync(V("1.9.0", "A,B,C,D"), Catalog));

        Assert.Contains("OCTO-CK202", refused.AllText());
        Assert.Contains("kept changing", refused.AllText());
        Assert.False(HasFile("1.9.0"));
        Assert.Equal(CkPublishGate.MaxAttempts, next - 1);
    }

    [Fact]
    public async Task A_concurrent_publish_under_another_spelling_is_caught_after_the_write_and_withdrawn()
    {
        // The case variant lives in its own directory and its own per-major index: the conditional write cannot see the
        // other publish. The check after the write does: 1.1.1 is judged against 1.1.0 as the catalog holds it now.
        await SeedBaselineAsync();
        const string variant = "gatemodel";
        OtherProcessWinsTheRaceOf("1.1.1", V("1.1.0", AddsDelta));

        var refused = await Assert.ThrowsAsync<CkPublishRefusedException>(() =>
            _a.PublishAsync(GateModels.Create("1.1.1", Baseline, 2, variant), Catalog));

        var text = refused.AllText();
        Assert.Contains("OCTO-CK200", text);
        Assert.Contains(CkPublishRefusal.ConcurrentPublishCode, text);
        Assert.Contains("withdrawn", text);
        Assert.True(HasFile("1.1.0"));
        Assert.False(HasFile("1.1.1", variant));
        Assert.Contains("1.1.1", _a.Repository.Files["ck-models/v3/g/gatemodel/withdrawn.json"]);
    }

    [Fact]
    public async Task Without_a_concurrent_publish_nothing_is_verified_away_and_the_files_equal_a_plain_publish()
    {
        await SeedBaselineAsync();

        await _a.PublishAsync(V("1.1.0", AddsDelta), Catalog);
        await _a.PublishAsync(V("1.1.1", AddsDelta), Catalog);

        Assert.True(HasFile("1.1.0"));
        Assert.True(HasFile("1.1.1"));
        Assert.False(_a.Repository.Files.ContainsKey("ck-models/v3/g/GateModel/withdrawn.json"));
    }
}
