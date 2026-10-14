using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6719: how many GitHub API requests a CK publish wave costs. A catalog of 60 models, then what a libs cascade plus
///     a 12-model CK publish does: ValidateVersion of 12 models (one process), the cascade check of 12 models (one
///     process each) and the publish of 12 models (the gate and the write, one process each).
/// </summary>
public sealed class CatalogApiBudgetTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task SeedCatalogAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            foreach (var version in new[] { "1.0.0", "1.1.0", "2.0.0" })
            {
                await _h.SeedAsync(GateModels.Create(version, name: $"Seed{i:D2}"), Catalog);
            }
        }

        for (var i = 0; i < 12; i++)
        {
            await _h.SeedAsync(GateModels.Create("1.0.0", name: $"Candidate{i:D2}"), Catalog);
        }

        _h.Restart();
        _h.ClearCatalogCache();
        _h.Repository.ResetApiCalls();
    }

    private string PublishBreakdown = "";

    private async Task<(int Validate, int Cascade, int Publish)> RunWaveAsync()
    {
        await SeedCatalogAsync();
        var repo = _h.Repository;

        // ValidateVersion: one process, baseline of each of the 12 candidates.
        var resolver = _h.Get<Meshmakers.Octo.ConstructionKit.Engine.SemVer.ICkBaselineResolver>();
        for (var i = 0; i < 12; i++)
        {
            await resolver.ResolveAsync($"Candidate{i:D2}", new CkVersion(1, 1, 0), Catalog);
        }

        var validate = repo.ApiCallCount;
        repo.ResetApiCalls();

        // Cascade check: one process per model; the dependents are listed from the source of truth when it can be asked.
        for (var i = 0; i < 12; i++)
        {
            _h.Restart();
            foreach (var catalog in _h.Get<IEnumerable<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog>>()
                         .Where(c => c.CatalogName == Catalog))
            {
                if (catalog is IAuthoritativeVersionCatalog { IsAuthoritativeReadAvailable: true } authoritative)
                {
                    await authoritative.ReadVersionsAsync(null);
                }
            }
        }

        var cascade = repo.ApiCallCount;
        repo.ResetApiCalls();

        // Publish: one process per model (gate read + write).
        for (var i = 0; i < 12; i++)
        {
            _h.Restart();
            await _h.PublishAsync(GateModels.Create("1.1.0", name: $"Candidate{i:D2}"), Catalog);
        }

        var publish = repo.ApiCallCount;
        var tracePath = Environment.GetEnvironmentVariable("CATALOG_BUDGET_TRACE");
        if (!string.IsNullOrEmpty(tracePath))
        {
            File.WriteAllLines(tracePath, repo.ApiTrace.Take(60));
        }

        PublishBreakdown = string.Join(", ", repo.ApiCalls.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
        return (validate, cascade, publish);
    }

    // Before AB#6719 (measured with this very scenario on engine main 9ab4fc34): validate=62 cascade=2484 publish=336
    // total=2882 requests for a catalog of 72 models; the cascade check alone read two root indexes plus a library and a
    // per-major index of every model, once per model of the repository. The ceilings below keep the wave far inside the
    // 25 percent (1250 requests) that a libs cascade plus a 12-model CK publish may spend of the hourly 5000.
    [Fact]
    public async Task A_12_model_wave_stays_inside_its_budget()
    {
        var (validate, cascade, publish) = await RunWaveAsync();

        Assert.True(validate <= 60, $"ValidateVersion of 12 models: {validate} requests");
        Assert.True(cascade <= 12 * 3, $"cascade check, one process per model: {cascade} requests");
        Assert.True(publish <= 12 * 26, $"publish gate and write, one process per model: {publish} requests");
        Assert.True(validate + cascade + publish <= 1250 / 2, $"{validate + cascade + publish} requests in total");
    }

    [Fact]
    public async Task The_whole_catalog_is_one_request_and_equals_what_the_indexes_say()
    {
        await SeedCatalogAsync();
        var catalog = _h.Get<IEnumerable<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog>>()
            .OfType<IAuthoritativeVersionCatalog>().Single(c => ((Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog)c).CatalogName == Catalog);
        _h.Repository.ResetApiCalls();

        var fromTree = await catalog.ReadVersionsAsync(null);
        var treeCalls = _h.Repository.ApiCallCount;
        _h.Repository.TreeTruncated = true; // GitHub could not return the complete tree: walk the indexes
        _h.Restart();
        var catalog2 = _h.Get<IEnumerable<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog>>()
            .OfType<IAuthoritativeVersionCatalog>().Single(c => ((Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog)c).CatalogName == Catalog);
        _h.Repository.ResetApiCalls();
        var fromIndexes = await catalog2.ReadVersionsAsync(null);

        Assert.True(treeCalls <= 2, $"{treeCalls} requests for the tree read");
        Assert.True(_h.Repository.ApiCallCount > 100, "the truncated tree fell back to the index walk");
        Assert.False(fromTree.SourceUnreachable);
        Assert.Equal(fromIndexes.Versions.Select(v => v.FullName), fromTree.Versions.Select(v => v.FullName));
        Assert.Equal(60 * 3 + 12, fromTree.Versions.Count);
    }

    [Fact]
    public async Task A_model_read_twice_in_one_process_costs_the_requests_once()
    {
        await SeedCatalogAsync();
        var catalog = _h.Get<IEnumerable<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog>>()
            .OfType<IAuthoritativeVersionCatalog>().Single(c => ((Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog)c).CatalogName == Catalog);
        _h.Repository.ResetApiCalls();

        var first = await catalog.ReadVersionsAsync("Candidate00");
        var firstCalls = _h.Repository.ApiCallCount;
        var second = await catalog.ReadVersionsAsync("Candidate00");
        var secondCalls = _h.Repository.ApiCallCount - firstCalls;
        var other = await catalog.ReadVersionsAsync("Candidate01");
        var otherCalls = _h.Repository.ApiCallCount - firstCalls - secondCalls;

        // The first read costs the repository state, the root indexes, the library and the major index of the model. The
        // second one only asks again for an index that does not exist (not memoised: another process may create it).
        Assert.True(firstCalls >= 4, $"{firstCalls} requests for the first read");
        Assert.True(secondCalls <= 1, $"{secondCalls} requests for the repeated read");
        Assert.True(otherCalls <= 3, $"{otherCalls} requests for a second model: its library and major index; the root indexes are memoised");
        Assert.Equal(first.Versions.Select(v => v.FullName), second.Versions.Select(v => v.FullName));
        Assert.Single(other.Versions);
    }

    [Fact]
    public async Task A_publish_of_this_process_drops_the_memo_so_the_next_read_sees_it()
    {
        await SeedCatalogAsync();
        var catalog = _h.Get<IEnumerable<Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog>>()
            .OfType<IAuthoritativeVersionCatalog>().Single(c => ((Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.ICatalog)c).CatalogName == Catalog);
        var before = await catalog.ReadVersionsAsync("Candidate00");

        await _h.PublishAsync(GateModels.Create("1.1.0", name: "Candidate00"), Catalog);
        var after = await catalog.ReadVersionsAsync("Candidate00");

        Assert.Single(before.Versions);
        Assert.Contains(after.Versions, v => v.Version.ToString() == "1.1.0");
    }

    [Fact]
    public async Task A_publish_that_lost_the_race_reads_the_repository_again_not_its_memo()
    {
        // The stale-mirror regression of AB#6588 must not come back through the memo: another process publishes between
        // the baseline read and the write; the aborted publish is re-judged against what the repository holds.
        await _h.SeedAsync(GateModels.Create("1.0.0"), Catalog);
        _h.Restart();
        var other = new GateHarness(repository: _h.Repository);
        try
        {
            _h.Repository.BeforeWrite = async _ =>
            {
                await other.PublishAsync(GateModels.Create("1.1.0", "A,B,C"), Catalog);
            };

            await Assert.ThrowsAnyAsync<Exception>(() => _h.PublishAsync(GateModels.Create("1.1.0", "A,B,X"), Catalog));

            // 1.1.0 of the other publisher won; the second publish is now judged against it (not against its stale memo).
            var result = await _h.EvaluateAsync(GateModels.Create("1.1.0", "A,B,X"), Catalog);
            Assert.Equal(CkPublishGateDecision.Refused, result.Decision);
        }
        finally
        {
            other.Dispose();
        }
    }

    [Fact]
    public async Task Measure_the_requests_of_a_12_model_wave()
    {
        var (validate, cascade, publish) = await RunWaveAsync();
        var line = $"validate={validate} cascade={cascade} publish={publish} total={validate + cascade + publish} (publish: {PublishBreakdown})";
        var path = Environment.GetEnvironmentVariable("CATALOG_BUDGET_OUT");
        if (!string.IsNullOrEmpty(path))
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }

        Assert.True(validate + cascade + publish > 0, line);
    }
}
