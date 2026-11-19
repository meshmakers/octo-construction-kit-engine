using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#9388: a refresh that read the catalog only partly (a root index was read, another index failed) never answers
///     as a complete inventory: ListAsync, SearchAsync and IsExistingAsync(CkModelId) for a missing version throw
///     OCTO-CK202 naming the catalog. An empty flagged cache (the mirror not reachable at all) answers as before, so
///     offline machines keep listing their local and embedded models. Real HTTP against a local server.
/// </summary>
public sealed class CatalogPartialCacheTests : IDisposable
{
    private const string RootIndex =
        "{\"models\":[{\"modelName\":\"Alpha\",\"catalogPath\":\"ck-models/v2/a/Alpha/catalog.json\"}," +
        "{\"modelName\":\"Beta\",\"catalogPath\":\"ck-models/v2/b/Beta/catalog.json\"}]}";

    private const string AlphaLibrary = "{\"modelId\":\"Alpha\",\"majorVersions\":[{\"majorVersion\":1}]}";

    private const string AlphaVersions =
        "{\"modelId\":\"Alpha\",\"majorVersion\":1,\"description\":\"Alpha model\"," +
        "\"versions\":[{\"version\":\"1.0.0\",\"filePath\":\"ck-models/v2/a/Alpha/1/ck-alpha-1.0.0.json\"}]}";

    private readonly string _cacheDirectory =
        Path.Combine(Path.GetTempPath(), "CatalogPartialCacheTests", Guid.NewGuid().ToString());

    public CatalogPartialCacheTests()
    {
        Directory.CreateDirectory(_cacheDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, true);
        }
    }

    // Alpha is read completely, Beta's library index answers 404: the cache is partial.
    private static string? PartialMirror(string path) =>
        path.EndsWith("/ck-models/v2/catalog.json", StringComparison.Ordinal) ? RootIndex
        : path.EndsWith("/ck-models/v2/a/Alpha/catalog.json", StringComparison.Ordinal) ? AlphaLibrary
        : path.EndsWith("/ck-models/v2/a/Alpha/1/catalog.json", StringComparison.Ordinal) ? AlphaVersions
        : "404";

    [Fact]
    public async Task Listing_a_partial_cache_fails_with_CK202_naming_the_catalog()
    {
        using var server = new SilentTcpServer(PartialMirror);
        var catalog = await RefreshedAsync(server);

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(async () =>
        {
            await foreach (var _ in catalog.ListAsync(null))
            {
            }
        });

        Assert.Contains("OCTO-CK202", exception.Message);
        Assert.Contains("'PrivateGitHubCatalog'", exception.Message);
        Assert.Contains("transient", exception.Message);
    }

    [Fact]
    public async Task Searching_a_partial_cache_fails_with_CK202()
    {
        using var server = new SilentTcpServer(PartialMirror);
        var catalog = await RefreshedAsync(server);

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(async () =>
        {
            await foreach (var _ in catalog.SearchAsync("Alpha", null))
            {
            }
        });

        Assert.Contains("OCTO-CK202", exception.Message);
    }

    [Fact]
    public async Task A_partial_cache_confirms_a_version_it_holds_and_cannot_deny_one_it_lacks()
    {
        using var server = new SilentTcpServer(PartialMirror);
        var catalog = await RefreshedAsync(server);

        Assert.True(await catalog.IsExistingAsync(new CkModelId("Alpha", "1.0.0")));
        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            catalog.IsExistingAsync(new CkModelId("Beta", "1.0.0")));
        Assert.Contains("OCTO-CK202", exception.Message);
    }

    [Fact]
    public async Task A_catalog_that_can_ask_the_repository_leaves_the_missing_version_to_the_source_of_truth()
    {
        // Its readers that need a complete answer complete the cache answer from the repository (AB#6588), so the cache
        // answers what it holds instead of throwing.
        using var server = new SilentTcpServer(PartialMirror);
        var catalog = await RefreshedAsync(server, apiReader: true);

        var listed = new List<CatalogResultItem>();
        await foreach (var item in catalog.ListAsync(null))
        {
            listed.Add(item);
        }

        Assert.Equal("Alpha-1.0.0", Assert.Single(listed).ModelId.FullName);
        Assert.False(await catalog.IsExistingAsync(new CkModelId("Beta", "1.0.0")));
    }

    [Fact]
    public async Task An_empty_flagged_cache_answers_as_before()
    {
        // The mirror answers 500 for the root index: unreachable, nothing read - the offline case.
        using var server = new SilentTcpServer(_ => "500");
        var catalog = await RefreshedAsync(server);

        var listed = new List<CatalogResultItem>();
        await foreach (var item in catalog.ListAsync(null))
        {
            listed.Add(item);
        }

        var found = new List<CatalogResultItem>();
        await foreach (var item in catalog.SearchAsync("Alpha", null))
        {
            found.Add(item);
        }

        Assert.Empty(listed);
        Assert.Empty(found);
        Assert.False(await catalog.IsExistingAsync(new CkModelId("Alpha", "1.0.0")));
    }

    [Fact]
    public async Task A_complete_cache_lists_everything_it_read()
    {
        using var server = new SilentTcpServer(path =>
            path.EndsWith("/ck-models/v2/catalog.json", StringComparison.Ordinal)
                ? "{\"models\":[{\"modelName\":\"Alpha\",\"catalogPath\":\"ck-models/v2/a/Alpha/catalog.json\"}]}"
                : PartialMirror(path));
        var catalog = await RefreshedAsync(server);

        var listed = new List<CatalogResultItem>();
        await foreach (var item in catalog.ListAsync(null))
        {
            listed.Add(item);
        }

        Assert.Equal("Alpha-1.0.0", Assert.Single(listed).ModelId.FullName);
        Assert.False(await catalog.IsExistingAsync(new CkModelId("Beta", "1.0.0")));
    }

    private async Task<PrivateGitHubCatalog> RefreshedAsync(SilentTcpServer server, bool apiReader = false)
    {
        var catalog = new PrivateGitHubCatalog(A.Fake<ICkJsonSerializer>(), new WrapperFactory(),
            A.Fake<IGitHubClientFactory>(), Options.Create(new PrivateGitHubCatalogOptions
            {
                CacheDirectory = _cacheDirectory,
                GitHubPagesUri = server.BaseUri.ToString(),
                GitHubRepositoryOwner = "testowner",
                GitHubRepositoryName = "testrepo",
                GitHubRepositoryBranch = "main",
                GitHubApiToken = apiReader ? "test-token" : null,
                ApiReads = apiReader ? CatalogApiReadMode.Enabled : CatalogApiReadMode.Disabled,
                RefreshNotFoundRetryCount = 0,
                RefreshNotFoundRetryDelaySeconds = 0
            }));
        await catalog.RefreshCatalogAsync(null, true, TestContext.Current.CancellationToken);
        return catalog;
    }

    private sealed class WrapperFactory : IHttpClientFactory
    {
        public IHttpClientWrapper CreateClient(Uri baseAddress) =>
            new HttpClientWrapper(baseAddress, TimeSpan.FromSeconds(5), [TimeSpan.Zero, TimeSpan.Zero]);
    }
}
