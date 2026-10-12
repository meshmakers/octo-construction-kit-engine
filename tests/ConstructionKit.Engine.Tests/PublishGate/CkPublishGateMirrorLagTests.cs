using System.Net;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6581: the gh-pages mirror lags the repository by up to a minute after a publish. The publish baseline and the
///     <c>Unpublish</c> dependents check read the repository, so a model published seconds ago is seen as it is. The tests
///     freeze the mirror at the state before the last publish (<see cref="InMemoryGitHubRepository.FreezePages" />) and start
///     from a cold cache (a fresh <c>ckc</c> process), the way the back-to-back runs of the AB#6391 gate slice did.
/// </summary>
public sealed class CkPublishGateMirrorLagTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private InMemoryGitHubRepository Repo => _h.Repository;

    private static CkCompiledModelRoot V2(string version, string enumValues = "A,B", string? name = null) =>
        GateModels.Create(version, enumValues, 2, name);

    private static CkCompiledModelRoot Consumer(string version, CkModelId pin) =>
        new()
        {
            ModelId = new CkModelId("Consumer", version), Description = "Consumer", Dependencies = [pin]
        };

    [Fact]
    public async Task A_version_published_seconds_ago_is_the_baseline_although_the_mirror_does_not_know_it()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.FreezePages(); // the mirror stays at 1.0.0 ...
        await _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog); // ... while 1.1.0 is published
        _h.ClearCatalogCache();

        // Removing C is only breaking against 1.1.0; against the stale 1.0.0 it would pass unnoticed.
        var result = await _h.EvaluateAsync(V2("1.2.0"), Catalog);
        var exception = await Assert.ThrowsAsync<CkPublishRefusedException>(() => _h.PublishAsync(V2("1.2.0"), Catalog));

        Assert.Equal(["OCTO-CK200"], GateHarness.ErrorCodes(result).Distinct());
        Assert.Equal("GateModel-1.1.0", result.Verdict!.BaselineId.FullName);
        Assert.Contains("OCTO-CK200", exception.AllText());
        Assert.False(Repo.Files.ContainsKey("ck-models/v3/g/GateModel/1/ck-gatemodel-1.2.0.json"));
    }

    [Fact]
    public async Task The_declared_version_published_seconds_ago_is_recognised_as_published()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.FreezePages();
        await _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog);
        _h.ClearCatalogCache();

        var identical = await _h.EvaluateAsync(V2("1.1.0", "A,B,C"), Catalog);
        var different = await _h.EvaluateAsync(V2("1.1.0", "A,B,D"), Catalog);

        Assert.Equal(CkPublishGateDecision.AlreadyPublished, identical.Decision);
        Assert.Equal(["OCTO-CK205"], GateHarness.ErrorCodes(different));
    }

    [Fact]
    public async Task A_dependent_published_seconds_ago_blocks_the_unpublish_of_its_dependency()
    {
        var provider = new CkModelId("GateModel", "1.0.0");
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.FreezePages();
        await _h.PublishAsync(Consumer("1.0.0", provider), Catalog);
        _h.ClearCatalogCache();
        var before = Repo.Snapshot();

        var plan = await _h.CatalogService.PlanUnpublishAsync(Catalog, provider);
        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            _h.CatalogService.UnpublishAsync(Catalog, provider, null));

        Assert.Equal("Consumer-1.0.0", Assert.Single(plan.Dependents).ModelId.FullName);
        Assert.Contains("Consumer-1.0.0", exception.Message);
        Assert.Equal(before, Repo.Snapshot());
    }

    [Fact]
    public async Task A_version_published_seconds_ago_can_be_unpublished_again()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        Repo.FreezePages();
        await _h.PublishAsync(V2("1.1.0", "A,B,C"), Catalog);
        _h.ClearCatalogCache();
        var published = new CkModelId("GateModel", "1.1.0");

        var plan = await _h.CatalogService.PlanUnpublishAsync(Catalog, published);
        var result = await _h.CatalogService.UnpublishAsync(Catalog, published, null);

        Assert.True(plan.IsPublished);
        Assert.Equal(CatalogUnpublishResult.Removed, result);
        Assert.False(Repo.Files.ContainsKey("ck-models/v3/g/GateModel/1/ck-gatemodel-1.1.0.json"));
    }

    [Fact]
    public async Task An_unreadable_catalog_fails_the_unpublish_with_CK202_and_changes_nothing()
    {
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        _h.ClearCatalogCache();
        Repo.ApiFault = _ => ModelCatalogException.GitHubRequestFailed("read", "ck-models/v3/catalog.json", 403,
            "API rate limit exceeded", 6, new ApiException("rate limit", HttpStatusCode.Forbidden));
        var before = Repo.Snapshot();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            _h.CatalogService.UnpublishAsync(Catalog, new CkModelId("GateModel", "1.0.0"), null));

        Assert.Contains("OCTO-CK202", exception.Message);
        Assert.Contains("rate limit", exception.Message);
        Assert.Equal(before, Repo.Snapshot());
    }

    [Fact]
    public async Task A_down_mirror_does_not_hide_a_dependent_from_the_unpublish_check()
    {
        var provider = new CkModelId("GateModel", "1.0.0");
        await _h.PublishAsync(V2("1.0.0"), Catalog);
        await _h.PublishAsync(Consumer("1.0.0", provider), Catalog);
        Repo.PagesBehavior = _ => PagesAnswer.NotFound;
        _h.ClearCatalogCache();

        var plan = await _h.CatalogService.PlanUnpublishAsync(Catalog, provider);

        Assert.Equal("Consumer-1.0.0", Assert.Single(plan.Dependents).ModelId.FullName);
    }
}
