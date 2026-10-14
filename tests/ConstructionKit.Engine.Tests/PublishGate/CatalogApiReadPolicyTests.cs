using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6719: a token alone does not switch a local build to the GitHub API. The reads of a process use the Pages mirror and
///     the caches unless it runs on CI (or <see cref="CatalogApiReadMode.Enabled" /> says so); the publish gate decision stays
///     authoritative whatever the read mode is.
/// </summary>
[Collection("CatalogApiReadPolicyEnvironment")]
public sealed class CatalogApiReadPolicyTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();
    private readonly Dictionary<string, string?> _saved = new();

    public CatalogApiReadPolicyTests()
    {
        foreach (var name in new[] { "TF_BUILD", "ContinuousIntegrationBuild", "CI", "OctoCkCatalogApiReads" })
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _h.Dispose();
    }

    private InMemoryGitHubRepository Repo => _h.Repository;

    [Fact]
    public void Auto_mode_reads_through_the_api_on_a_ci_build_only()
    {
        var options = new PrivateGitHubCatalogOptions { GitHubApiToken = "t" };

        Assert.False(options.ApiReadsEnabled); // developer machine with a token configured

        Environment.SetEnvironmentVariable("TF_BUILD", "True");
        Assert.True(options.ApiReadsEnabled);
        Environment.SetEnvironmentVariable("TF_BUILD", null);

        Environment.SetEnvironmentVariable("ContinuousIntegrationBuild", "true");
        Assert.True(options.ApiReadsEnabled);
    }

    [Theory]
    [InlineData("Enabled", true)]
    [InlineData("Disabled", false)]
    [InlineData("enabled", true)]
    [InlineData("nonsense", false)]
    public void The_mode_can_be_forced_either_way(string value, bool expected)
    {
        var options = new PrivateGitHubCatalogOptions { ApiReads = GitHubCatalogOptions.ParseApiReads(value) ?? CatalogApiReadMode.Auto };

        Assert.Equal(expected, options.ApiReadsEnabled);
    }

    [Fact]
    public async Task A_local_process_with_a_token_reads_no_api_for_the_dependency_baseline()
    {
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        Repo.ResetApiCalls();
        var local = new RepositoryBackedCatalog(_h.Get<Meshmakers.Octo.ConstructionKit.Contracts.Serialization.ICkJsonSerializer>(),
            Repo, Path.Combine(_h.Root, "cache"), isLane: false, apiReads: CatalogApiReadMode.Auto);

        Assert.False(local.IsAuthoritativeReadAvailable);
    }

    [Fact]
    public async Task The_Local_baseline_never_asks_the_repository_even_when_reads_are_enabled()
    {
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        _h.Restart();
        Repo.ResetApiCalls();

        var resolution = await _h.Get<ICkBaselineResolver>()
            .ResolveAsync(GateModels.Name, new CkVersion(1, 1, 0), CkBaselineSource.Local);

        Assert.Equal(0, Repo.ApiCallCount);
        Assert.False(resolution.SourceUnreachable);
    }

    [Fact]
    public async Task The_Remote_baseline_asks_the_repository_when_reads_are_enabled()
    {
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        _h.Restart();
        Repo.ResetApiCalls();

        var resolution = await _h.Get<ICkBaselineResolver>()
            .ResolveAsync(GateModels.Name, new CkVersion(1, 1, 0), CkBaselineSource.Remote);

        Assert.True(Repo.ApiCallCount > 0);
        Assert.Equal(new CkVersion(1, 0, 0), resolution.Baseline?.Version);
    }

    [Fact]
    public async Task A_publish_decides_against_the_repository_whatever_the_read_mode_is()
    {
        // The gate decision is authoritative: with API reads Disabled the baseline still comes from the repository.
        var h = new GateHarness(apiReads: CatalogApiReadMode.Disabled);
        try
        {
            await h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
            h.Restart();
            h.Repository.ResetApiCalls();

            var result = await h.EvaluateAsync(GateModels.Create("1.1.0", "A,B,C"), Catalog);

            Assert.True(h.Repository.ApiCallCount > 0, "the gate read the repository");
            Assert.False(result.HasErrors, string.Join("; ", result.Messages.Select(m => m.Text)));
        }
        finally
        {
            h.Dispose();
        }
    }

    [Fact]
    public async Task A_catalog_without_a_token_never_asks_the_api()
    {
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        _h.Restart();
        Repo.ResetApiCalls();
        var anonymous = new RepositoryBackedCatalog(
            _h.Get<Meshmakers.Octo.ConstructionKit.Contracts.Serialization.ICkJsonSerializer>(), Repo,
            Path.Combine(_h.Root, "cache"), apiReads: CatalogApiReadMode.Enabled, token: null);

        Assert.False(anonymous.IsAuthoritativeReadAvailable);
        await anonymous.RefreshCatalogAsync(null, true);
        Assert.True(await anonymous.IsExistingAsync(new CkModelId(GateModels.Name, "1.0.0")));
        Assert.Equal(0, Repo.ApiCallCount);
    }

    [Fact]
    public async Task An_exhausted_limit_is_named_in_the_CK202_text_of_the_gate()
    {
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        _h.Restart();
        Repo.ApiFault = _ => new GitHubRateLimitExhaustedException(DateTimeOffset.UtcNow.AddMinutes(40));

        var result = await _h.EvaluateAsync(GateModels.Create("1.1.0", "A,B,C"), Catalog);

        Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        var message = result.Messages.Single(m => m.Code == "OCTO-CK202").Text;
        Assert.Matches(@"GitHub rate limit exhausted, resets at \d\d:\d\dZ", message);
    }
}
