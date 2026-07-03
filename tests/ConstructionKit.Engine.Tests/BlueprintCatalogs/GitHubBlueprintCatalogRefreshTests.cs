using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Refresh-path tests for <see cref="GitHubBlueprintCatalog" /> failure semantics (AB#4309): a transport
/// failure during an explicit refresh must propagate (so the catalog manager reports Failed) and must NOT
/// replace a previously good cache with an empty one, while the lazy cache bootstrap on the read path stays
/// best-effort and keeps serving an empty catalog.
/// </summary>
public sealed class GitHubBlueprintCatalogRefreshTests : IDisposable
{
    private const string BlueprintName = "MyBp";

    private readonly IHttpClientWrapper _httpClientWrapper = A.Fake<IHttpClientWrapper>();
    private readonly string _tempDir;
    private readonly PrivateGitHubBlueprintCatalog _catalog;

    public GitHubBlueprintCatalogRefreshTests()
    {
        var serializer = A.Fake<IBlueprintSerializer>();
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();

        _tempDir = Path.Combine(Path.GetTempPath(), nameof(GitHubBlueprintCatalogRefreshTests),
            Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var options = new PrivateGitHubBlueprintCatalogOptions
        {
            CacheDirectory = _tempDir,
            GitHubApiToken = "test-token"
        };

        A.CallTo(() => httpClientFactory.CreateClient(A<Uri>._)).Returns(_httpClientWrapper);

        _catalog = new PrivateGitHubBlueprintCatalog(serializer, httpClientFactory, gitHubClientFactory,
            Options.Create(options));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void SetupHealthyRemote()
    {
        A.CallTo(() => _httpClientWrapper.GetStringAsync(A<string>.That.Matches(u =>
                u.EndsWith("/catalog.json") && !u.Contains(BlueprintName))))
            .Returns($$"""
                       {"version":"1.0","updatedAt":"2026-07-01T00:00:00Z","blueprints":
                       [{"blueprintName":"{{BlueprintName}}","catalogPath":"blueprints/v1/m/{{BlueprintName}}/catalog.json"}]}
                       """);
        A.CallTo(() => _httpClientWrapper.GetStringAsync(A<string>.That.Matches(u =>
                u.EndsWith($"/m/{BlueprintName}/catalog.json"))))
            .Returns($$"""
                       {"version":"1.0","blueprintId":"{{BlueprintName}}","updatedAt":"2026-07-01T00:00:00Z","majorVersions":
                       [{"majorVersion":1,"catalogPath":"blueprints/v1/m/{{BlueprintName}}/1/catalog.json"}]}
                       """);
        A.CallTo(() => _httpClientWrapper.GetStringAsync(A<string>.That.Matches(u =>
                u.EndsWith($"/m/{BlueprintName}/1/catalog.json"))))
            .Returns($$"""
                       {"version":"1.0","blueprintId":"{{BlueprintName}}","majorVersion":1,"description":"Test blueprint",
                       "updatedAt":"2026-07-01T00:00:00Z","versions":
                       [{"version":"1.0.0","directoryPath":"m/{{BlueprintName}}/1/1.0.0","publishedAt":"2026-07-01T00:00:00Z"}]}
                       """);
    }

    private void SetupUnreachableRemote()
    {
        A.CallTo(() => _httpClientWrapper.GetStringAsync(A<string>._))
            .Throws(new HttpRequestException("network down"));
    }

    private async Task<List<BlueprintCatalogResultItem>> ListCatalogAsync()
    {
        var items = new List<BlueprintCatalogResultItem>();
        await foreach (var item in _catalog.ListAsync(null))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task RefreshCatalogAsync_TransportFailure_ThrowsAndPreservesExistingCache()
    {
        // Seed a good cache from a healthy remote.
        SetupHealthyRemote();
        await _catalog.RefreshCatalogAsync(forceRefresh: true);
        Assert.Contains(await ListCatalogAsync(), i => i.BlueprintId.FullName == $"{BlueprintName}-1.0.0");

        // The remote becomes unreachable: an explicit refresh must fail loudly ...
        SetupUnreachableRemote();
        await Assert.ThrowsAsync<HttpRequestException>(() => _catalog.RefreshCatalogAsync(forceRefresh: true));

        // ... and must not have wiped the previously good cache.
        Assert.Contains(await ListCatalogAsync(), i => i.BlueprintId.FullName == $"{BlueprintName}-1.0.0");
    }

    [Fact]
    public async Task ListAsync_TransportFailureWithoutCache_ReturnsEmptyInsteadOfThrowing()
    {
        SetupUnreachableRemote();

        // The lazy cache bootstrap on the read path stays best-effort.
        Assert.Empty(await ListCatalogAsync());
    }
}
