using System.Net;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// In-memory cache of the parsed blueprint manifest in <see cref="GitHubBlueprintCatalog.GetAsync" /> (AB#6306).
/// </summary>
public sealed class GitHubBlueprintCatalogMetaCacheTests : IDisposable
{
    private static readonly BlueprintId Id = new("MyBp", "1.0.0");

    private readonly IHttpClientWrapper _http = A.Fake<IHttpClientWrapper>();
    private readonly IBlueprintSerializer _serializer = A.Fake<IBlueprintSerializer>();
    private readonly IGitHubClientWrapper _gitHub = A.Fake<IGitHubClientWrapper>();
    private readonly string _tempDir;
    private readonly PrivateGitHubBlueprintCatalog _catalog;

    public GitHubBlueprintCatalogMetaCacheTests()
    {
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();

        _tempDir = Path.Combine(Path.GetTempPath(), nameof(GitHubBlueprintCatalogMetaCacheTests),
            Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var options = new PrivateGitHubBlueprintCatalogOptions { CacheDirectory = _tempDir, GitHubApiToken = "t" };
        A.CallTo(() => httpClientFactory.CreateClient(A<Uri>._)).Returns(_http);
        A.CallTo(() => gitHubClientFactory.CreateClient(A<IGitHubOptions>._)).Returns(_gitHub);

        A.CallTo(() => _serializer.DeserializeBlueprintMetaAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily(() => Task.FromResult(new BlueprintMetaRootDto
            {
                BlueprintId = Id,
                CkModelDependencies = [],
                BlueprintDependencies = []
            }));
        A.CallTo(() => _http.GetAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("name: MyBp")
            }));
        // Empty remote catalog / nothing to prune or delete.
        A.CallTo(() => _http.GetStringAsync(A<string?>._)).Returns("");
        A.CallTo(() => _gitHub.ListFilesRecursiveAsync(A<string>._))
            .Returns(new List<(string, string)>());

        _catalog = new PrivateGitHubBlueprintCatalog(_serializer, httpClientFactory, gitHubClientFactory,
            Options.Create(options));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private int ManifestFetches =>
        Fake.GetCalls(_http).Count(c => c.Method.Name == nameof(IHttpClientWrapper.GetAsync));

    [Fact]
    public async Task GetAsync_SecondCall_DoesNotHitHttp()
    {
        var first = await _catalog.GetAsync(Id, new OperationResult());
        var second = await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(1, ManifestFetches);
        Assert.Equal(Id, second.BlueprintId);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task GetAsync_CallerMutatingResult_DoesNotAffectCache()
    {
        var first = await _catalog.GetAsync(Id, new OperationResult());
        first.BlueprintDependencies!.Add(new BlueprintIdVersionRange("Other-[1.0.0,)"));

        var second = await _catalog.GetAsync(Id, new OperationResult());

        Assert.Empty(second.BlueprintDependencies!);
    }

    [Fact]
    public async Task GetAsync_Failure_IsNotCached()
    {
        A.CallTo(() => _http.GetAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<BlueprintCatalogException>(() => _catalog.GetAsync(Id, new OperationResult()));

        A.CallTo(() => _http.GetAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("name: MyBp")
            }));
        var meta = await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(Id, meta.BlueprintId);
        Assert.Equal(2, ManifestFetches);
    }

    [Fact]
    public async Task UnpublishAsync_InvalidatesCachedManifest()
    {
        await _catalog.GetAsync(Id, new OperationResult());
        await _catalog.UnpublishAsync(Id);
        await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(2, ManifestFetches);
    }

    [Fact]
    public async Task UnpublishAllVersionsAsync_InvalidatesCachedManifestOfPublishedVersions()
    {
        // The version index (read through the GitHub client) lists MyBp 1.0.0 as published.
        A.CallTo(() => _gitHub.GetFileAsync(A<string>.That.EndsWith("/m/MyBp/catalog.json")))
            .Returns(Task.FromResult<(string, string)?>(("""{"version":"1.0","blueprintId":"MyBp","updatedAt":"2026-07-01T00:00:00Z","majorVersions":[{"majorVersion":1,"catalogPath":"x"}]}""", "sha1")));
        A.CallTo(() => _gitHub.GetFileAsync(A<string>.That.EndsWith("/m/MyBp/1/catalog.json")))
            .Returns(Task.FromResult<(string, string)?>(("""{"version":"1.0","blueprintId":"MyBp","majorVersion":1,"updatedAt":"2026-07-01T00:00:00Z","versions":[{"version":"1.0.0","directoryPath":"m/MyBp/1/1.0.0","publishedAt":"2026-07-01T00:00:00Z"}]}""", "sha2")));

        await _catalog.GetAsync(Id, new OperationResult());
        await _catalog.UnpublishAllVersionsAsync("MyBp");
        await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(2, ManifestFetches);
    }
}
