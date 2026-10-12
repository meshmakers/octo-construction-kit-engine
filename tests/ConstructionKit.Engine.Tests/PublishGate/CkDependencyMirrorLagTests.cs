using System.Net;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6588: the dependency resolution of a publish, <c>ValidateVersion -rf</c> and the cascade check used to read the
///     lagging Pages mirror while the publish gate read the repository (the catalogs release train 3.5.16 published
///     <c>System-2.5.0</c> and then failed on <c>System.Notification</c>: "Dependencies 'System-[2.5.0]' are unknown").
///     The tests freeze the mirror before the last publish and start from a cold cache, like back-to-back runs of a train.
/// </summary>
public sealed class CkDependencyMirrorLagTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private InMemoryGitHubRepository Repo => _h.Repository;

    private static CkCompiledModelRoot Provider(string version, string enumValues = "A,B") =>
        GateModels.Create(version, enumValues, 2);

    private static CkCompiledModelRoot Consumer(string version, CkModelId pin) =>
        new() { ModelId = new CkModelId("Consumer", version), Description = "Consumer", Dependencies = [pin] };

    private async Task PublishProviderWithLaggingMirrorAsync()
    {
        await _h.PublishAsync(Provider("1.0.0"), Catalog);
        Repo.FreezePages(); // the mirror stays at 1.0.0 ...
        await _h.PublishAsync(Provider("1.1.0", "A,B,C"), Catalog); // ... while 1.1.0 is published
        _h.ClearCatalogCache();
    }

    [Fact]
    public async Task A_dependency_published_seconds_ago_resolves_although_the_mirror_does_not_know_it()
    {
        await PublishProviderWithLaggingMirrorAsync();

        await _h.PublishAsync(Consumer("1.0.0", new CkModelId("GateModel", "1.1.0")), Catalog);

        Assert.Contains(Repo.Files.Keys, k => k.Contains("ck-consumer-1.0.0.json"));
    }

    [Fact]
    public async Task A_dependency_that_no_catalog_holds_is_still_unknown()
    {
        await PublishProviderWithLaggingMirrorAsync();

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            _h.PublishAsync(Consumer("1.0.0", new CkModelId("GateModel", "1.2.0")), Catalog));

        Assert.Contains("unknown construction kit model", exception.Message);
        Assert.DoesNotContain(Repo.Files.Keys, k => k.Contains("ck-consumer-1.0.0.json"));
    }

    [Fact]
    public async Task An_unreadable_repository_fails_the_dependency_resolution_closed_with_CK202()
    {
        await PublishProviderWithLaggingMirrorAsync();
        Repo.ApiFault = _ => ModelCatalogException.GitHubRequestFailed("read", "ck-models/v3/catalog.json", 403,
            "API rate limit exceeded", 6, new ApiException("rate limit", HttpStatusCode.Forbidden));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            _h.PublishAsync(Consumer("1.0.0", new CkModelId("GateModel", "1.1.0")), Catalog));

        Assert.Contains("OCTO-CK202", exception.Message);
        Assert.DoesNotContain(Repo.Files.Keys, k => k.Contains("ck-consumer-1.0.0.json"));
    }

    [Fact]
    public async Task A_dependency_the_mirror_knows_needs_no_repository_read()
    {
        await _h.PublishAsync(Provider("1.0.0"), Catalog); // the cache is fresh and shows 1.0.0
        Repo.ApiFault = _ => new InvalidOperationException("the repository must not be read when the mirror knows the version");
        var resolved = await _h.Get<Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs.ICatalogManager>()
            .IsExistingAsync(new CkModelIdVersionRange("GateModel", "[1.0.0]"));

        Assert.True(resolved.Exists);
        Assert.Equal("GateModel-1.0.0", resolved.ModelId!.FullName);
    }

    [Fact]
    public async Task ValidateVersion_baseline_is_the_version_published_seconds_ago()
    {
        await PublishProviderWithLaggingMirrorAsync();

        var resolution = await _h.Get<ICkBaselineResolver>()
            .ResolveAsync("GateModel", new Meshmakers.Octo.ConstructionKit.Contracts.CkVersion("1.2.0"), CkBaselineSource.Remote);

        Assert.Equal("GateModel-1.1.0", resolution.Baseline?.FullName);
        Assert.False(resolution.SourceUnreachable);
    }

    [Fact]
    public async Task ValidateVersion_with_an_unreadable_repository_is_unreachable_not_a_first_publication()
    {
        await PublishProviderWithLaggingMirrorAsync();
        Repo.ApiFault = _ => ModelCatalogException.GitHubRequestFailed("read", "ck-models/v3/catalog.json", 403,
            "API rate limit exceeded", 6, new ApiException("rate limit", HttpStatusCode.Forbidden));

        var resolution = await _h.Get<ICkBaselineResolver>()
            .ResolveAsync("GateModel", new Meshmakers.Octo.ConstructionKit.Contracts.CkVersion("1.2.0"), CkBaselineSource.Remote);

        Assert.Null(resolution.Baseline);
        Assert.True(resolution.SourceUnreachable);
    }

    [Fact]
    public async Task The_cascade_check_sees_a_dependent_published_seconds_ago()
    {
        var provider = new CkModelId("GateModel", "1.0.0");
        await _h.PublishAsync(Provider("1.0.0"), Catalog);
        Repo.FreezePages();
        await _h.PublishAsync(Consumer("1.0.0", provider), Catalog);
        _h.ClearCatalogCache();

        var result = await _h.Get<ICkCascadeService>().AnalyzeAsync(Provider("1.1.0", "A"), Catalog);

        Assert.Contains(result.Dependents, d => d.Dependent.FullName == "Consumer-1.0.0");
    }
}
