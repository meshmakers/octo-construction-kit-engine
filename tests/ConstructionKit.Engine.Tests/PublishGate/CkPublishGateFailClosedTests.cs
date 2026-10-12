using System.Net;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6580: the publish gate fails closed. These tests run the REAL <see cref="GitHubCatalog" /> reader, resolver, verdict
///     service and gate over an in-memory repository whose two read paths misbehave the way GitHub does: the gh-pages
///     mirror answers 404 / 5xx / times out, the Contents API fails with a rate limit, a timeout or a transport error, the
///     repository or its branch does not exist. Not one of them may end as "first publication"; only a repository that
///     exists and does not hold the model is one.
/// </summary>
public sealed class CkPublishGateFailClosedTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static CkCompiledModelRoot V2(string version, string enumValues = "A,B", string? name = null) =>
        GateModels.Create(version, enumValues, 2, name);

    private InMemoryGitHubRepository Repo => _h.Repository;

    private Task<CkPublishGateResult> EvaluateAsync(CkCompiledModelRoot model) => _h.EvaluateAsync(model, Catalog);

    /// <summary>1.0.0 (A,B) and 1.1.0 (A,B,C) are published; the breaking 1.2.0 below removes B and C.</summary>
    private async Task PublishHistoryAsync()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        await _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog);
    }

    private static CkCompiledModelRoot Breaking() => V2("1.2.0", "A");

    private static Exception RateLimited(string path) =>
        ModelCatalogException.GitHubRequestFailed("read", path, 403, "API rate limit exceeded for user", 6,
            new ApiException("rate limit", HttpStatusCode.Forbidden));

    public static TheoryData<PagesAnswer> MirrorFailures =>
        new() { PagesAnswer.NotFound, PagesAnswer.ServerError, PagesAnswer.Timeout, PagesAnswer.Offline };

    [Theory]
    [MemberData(nameof(MirrorFailures))]
    public async Task A_failing_Pages_mirror_does_not_hide_the_baseline_a_breaking_version_is_refused(PagesAnswer answer)
    {
        await PublishHistoryAsync();
        Repo.PagesBehavior = _ => answer;
        _h.ClearCatalogCache(); // a fresh ckc process: cold cache, the mirror is the only thing it could have read
        var before = Repo.Snapshot();

        var result = await EvaluateAsync(Breaking());
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(Breaking(), Catalog));

        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(result).Distinct());
        Assert.Equal("GateModel-1.1.0", result.Verdict!.BaselineId.FullName);
        Assert.False(result.Verdict.Resolution.IsFirstPublication);
        Assert.Contains("OCTO-CK200", exception.AllText());
        Assert.Equal(before, Repo.Snapshot());
    }

    [Fact]
    public async Task A_Pages_mirror_that_is_down_does_not_block_an_additive_publish()
    {
        await PublishHistoryAsync();
        Repo.PagesBehavior = _ => PagesAnswer.NotFound;
        _h.ClearCatalogCache();

        await _h.PublishAsync(V2("1.2.0", "A,B,C,D"), Catalog);

        Assert.True(Repo.Files.ContainsKey("ck-models/v3/g/GateModel/1/ck-gatemodel-1.2.0.json"));
    }

    [Fact]
    public async Task A_rate_limited_Contents_API_is_CK202_and_nothing_is_written()
    {
        await PublishHistoryAsync();
        Repo.ApiFault = RateLimited;
        var before = Repo.Snapshot();

        var result = await EvaluateAsync(Breaking());
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(Breaking(), Catalog));

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("rate limit", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
        Assert.Contains("OCTO-CK202", exception.AllText());
        Assert.Equal(before, Repo.Snapshot());
    }

    [Fact]
    public async Task A_rate_limit_on_one_index_file_only_is_CK202_not_an_empty_catalog()
    {
        await PublishHistoryAsync();
        Repo.ApiFault = path => path.EndsWith("/1/catalog.json", StringComparison.Ordinal) ? RateLimited(path) : null;

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task An_offline_Contents_API_is_CK202_and_nothing_is_written()
    {
        await PublishHistoryAsync();
        Repo.ApiFault = _ => new HttpRequestException("Connection refused (127.0.0.1:9)");
        var before = Repo.Snapshot();

        var result = await EvaluateAsync(Breaking());
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(Breaking(), Catalog));

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("Connection refused", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
        Assert.Contains("OCTO-CK202", exception.AllText());
        Assert.Equal(before, Repo.Snapshot());
    }

    [Fact]
    public async Task A_timing_out_Contents_API_is_CK202()
    {
        await PublishHistoryAsync();
        Repo.ApiFault = _ => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("timed out", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
    }

    [Fact]
    public async Task A_repository_that_does_not_exist_is_CK202_not_a_first_publication()
    {
        Repo.State = GitHubRepositoryState.RepositoryNotFound;

        var result = await EvaluateAsync(Breaking());
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(Breaking(), Catalog));

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("does not exist", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
        Assert.Contains("OCTO-CK202", exception.AllText());
        Assert.Empty(Repo.Files);
    }

    [Fact]
    public async Task A_branch_that_does_not_exist_is_CK202_not_a_first_publication()
    {
        Repo.State = GitHubRepositoryState.BranchNotFound;

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("branch", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
    }

    [Theory]
    [InlineData(GitHubRepositoryState.Ready)]
    [InlineData(GitHubRepositoryState.Empty)]
    public async Task A_catalog_that_exists_and_holds_no_index_is_a_genuine_first_publication(GitHubRepositoryState state)
    {
        Repo.State = state;
        Repo.PagesBehavior = _ => PagesAnswer.NotFound;
        _h.ClearCatalogCache();

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Contains(result.Messages, m => m.Text.Contains("first publication"));
    }

    [Fact]
    public async Task A_model_that_the_existing_catalog_does_not_hold_is_a_genuine_first_publication()
    {
        await _h.PublishAsync(V2("1.0.0", name: "OtherModel"), Catalog);
        Repo.PagesBehavior = _ => PagesAnswer.NotFound;
        _h.ClearCatalogCache();

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(CkPublishGateDecision.Proceed, result.Decision);
        Assert.Contains(result.Messages, m => m.Text.Contains("first publication"));
    }

    [Fact]
    public async Task An_index_that_lists_the_model_but_whose_sub_index_is_missing_is_CK202()
    {
        await PublishHistoryAsync();
        Repo.Files.Remove("ck-models/v3/g/GateModel/1/catalog.json");

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
        Assert.Contains("does not exist", result.Messages.Single(m => m.Code == "OCTO-CK202").Text);
    }

    [Fact]
    public async Task An_index_that_cannot_be_parsed_is_CK202()
    {
        await PublishHistoryAsync();
        Repo.Files["ck-models/v3/catalog.json"] = "<html>502 Bad Gateway</html>";

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task A_baseline_file_that_the_index_lists_but_the_repository_lacks_is_CK202()
    {
        await PublishHistoryAsync();
        Repo.Files.Remove("ck-models/v3/g/GateModel/1/ck-gatemodel-1.1.0.json");

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK202"], GateHarness.ErrorCodes(result));
    }

    [Fact]
    public async Task The_cache_filled_from_the_mirror_cannot_turn_a_catalog_into_an_empty_one_for_the_gate()
    {
        await PublishHistoryAsync();
        // The cache of a reader that refreshed while the mirror was being replaced: empty and not flagged.
        var catalog = _h.Get<IEnumerable<Contracts.ModelCatalogs.ICatalog>>().Single(c => c.CatalogName == Catalog);
        Repo.PagesBehavior = _ => PagesAnswer.NotFound;
        await catalog.RefreshCatalogAsync(null, true);

        var result = await EvaluateAsync(Breaking());

        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(result).Distinct());
    }
}
