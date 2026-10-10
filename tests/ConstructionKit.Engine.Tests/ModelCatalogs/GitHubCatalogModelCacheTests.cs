using System.Net;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// In-memory cache of the downloaded compiled model payload in <see cref="GitHubCatalog.GetAsync" /> (AB#6330).
/// </summary>
public sealed class GitHubCatalogModelCacheTests : IDisposable
{
    private const string ModelUrl = "ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json";
    private static readonly CkModelId Id = new("TestModel", "1.0.0");

    private readonly IHttpClientWrapper _http = A.Fake<IHttpClientWrapper>();
    private readonly IGitHubClientWrapper _gitHub = A.Fake<IGitHubClientWrapper>();
    private readonly ICkJsonSerializer _serializer = A.Fake<ICkJsonSerializer>();
    private readonly string _tempDir;
    private readonly PublicGitHubCatalog _catalog;

    public GitHubCatalogModelCacheTests()
    {
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();
        _tempDir = Path.Combine(Path.GetTempPath(), nameof(GitHubCatalogModelCacheTests), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        var options = new PublicGitHubCatalogOptions
        {
            CacheDirectory = _tempDir,
            GitHubPagesUri = "https://test.github.io/repo",
            GitHubRepositoryOwner = "o",
            GitHubRepositoryName = "r",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = "t",
            RefreshNotFoundRetryCount = 0,
            RefreshNotFoundRetryDelaySeconds = 0
        };
        A.CallTo(() => httpClientFactory.CreateClient(A<Uri>._)).Returns(_http);
        A.CallTo(() => gitHubClientFactory.CreateClient(A<GitHubCatalogOptions>._)).Returns(_gitHub);

        // v3 root does not exist; v2 serves the model.
        A.CallTo(() => _http.GetAsync(A<string>.That.StartsWith("ck-models/v3/"), A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        StubModelOk();
        A.CallTo(() => _serializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, A<OperationResult>._,
                A<bool>._))
            .ReturnsLazily(() => new CkCompiledModelRoot { ModelId = Id, Description = "d" });

        _catalog = new PublicGitHubCatalog(_serializer, httpClientFactory, gitHubClientFactory, Options.Create(options));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void StubModelOk() =>
        A.CallTo(() => _http.GetAsync(ModelUrl, A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

    private int ModelFetches =>
        Fake.GetCalls(_http).Count(c => c.Method.Name == nameof(IHttpClientWrapper.GetAsync) &&
                                        (string?)c.Arguments[0] == ModelUrl);

    [Fact]
    public async Task GetAsync_SecondCall_DoesNotHitHttp_AndReturnsIndependentInstance()
    {
        var first = await _catalog.GetAsync(Id, new OperationResult());
        var second = await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(1, ModelFetches);
        Assert.Equal(Id, second.ModelId);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task GetAsync_NotFound_IsNotCached()
    {
        A.CallTo(() => _http.GetAsync(ModelUrl, A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(Id, new OperationResult()));

        StubModelOk();
        var model = await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(Id, model.ModelId);
        Assert.Equal(2, ModelFetches);
    }

    [Fact]
    public async Task GetAsync_ServerError_IsNotCached()
    {
        A.CallTo(() => _http.GetAsync(ModelUrl, A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(Id, new OperationResult()));

        StubModelOk();
        await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(2, ModelFetches);
    }

    [Fact]
    public async Task GetAsync_DeserializationErrors_AreNotCached()
    {
        A.CallTo(() => _serializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, A<OperationResult>._,
                A<bool>._))
            .ReturnsLazily((Stream _, string _, OperationResult r, bool _) =>
            {
                r.AddMessage(new OperationMessage(MessageLevel.Error, null, 1, "broken"));
                return new CkCompiledModelRoot { ModelId = Id };
            });
        await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(Id, new OperationResult()));
        await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(Id, new OperationResult()));

        Assert.Equal(2, ModelFetches);
    }

    [Fact]
    public async Task PublishAsync_InvalidatesCachedModel()
    {
        await _catalog.GetAsync(Id, new OperationResult());
        A.CallTo(() => _gitHub.GetFileAsync(A<string>._)).Returns(Task.FromResult<(string, string)?>(("{}", "sha")));

        await _catalog.PublishAsync(new CkCompiledModelRoot { ModelId = Id, Description = "d" }, force: true);
        await _catalog.GetAsync(Id, new OperationResult());

        Assert.Equal(2, ModelFetches);
    }

    [Fact]
    public async Task GetAsync_OctokitPath_SecondCall_DoesNotCallGitHub()
    {
        var gitHub = A.Fake<IGitHubClientWrapper>();
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();
        A.CallTo(() => gitHubClientFactory.CreateClient(A<GitHubCatalogOptions>._)).Returns(gitHub);
        A.CallTo(() => gitHub.GetFileAsync(A<string>.That.EndsWith("ck-testmodel-1.0.0.json")))
            .Returns(Task.FromResult<(string, string)?>(("{}", "sha")));
        var options = new PrivateGitHubCatalogOptions
        {
            CacheDirectory = _tempDir, GitHubApiToken = "t", GitHubRepositoryOwner = "o", GitHubRepositoryName = "r",
            GitHubRepositoryBranch = "main", GitHubPagesUri = ""
        };
        var catalog = new PrivateGitHubCatalog(_serializer, A.Fake<IHttpClientFactory>(), gitHubClientFactory,
            Options.Create(options));

        await catalog.GetAsync(Id, new OperationResult());
        await catalog.GetAsync(Id, new OperationResult());

        A.CallTo(() => gitHub.GetFileAsync(A<string>.That.EndsWith("ck-testmodel-1.0.0.json")))
            .MustHaveHappenedOnceExactly();
    }
}
