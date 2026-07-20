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
/// Base class for PublicGitHubCatalog tests with common setup
/// </summary>
public abstract class PublicGitHubCatalogTestsBase
{
    protected readonly PublicGitHubCatalog Catalog;
    protected readonly ICkJsonSerializer CkJsonSerializer;
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly IHttpClientWrapper HttpClientWrapper;
    protected readonly IGitHubClientFactory GitHubClientFactory;
    protected readonly IGitHubClientWrapper GitHubClientWrapper;
    protected readonly PublicGitHubCatalogOptions CatalogOptions;

    protected PublicGitHubCatalogTestsBase(bool withToken)
    {
        CkJsonSerializer = A.Fake<ICkJsonSerializer>();
        GitHubClientFactory = A.Fake<IGitHubClientFactory>();
        GitHubClientWrapper = A.Fake<IGitHubClientWrapper>();
        HttpClientFactory = A.Fake<IHttpClientFactory>();
        HttpClientWrapper = A.Fake<IHttpClientWrapper>();

        var tempDirectory = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);

        CatalogOptions = new PublicGitHubCatalogOptions
        {
            CacheDirectory = tempDirectory,
            GitHubPagesUri = "https://test.github.io/repo",
            GitHubRepositoryOwner = "testowner",
            GitHubRepositoryName = "testrepo",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = withToken ? "test-token" : null
        };

        A.CallTo(() => HttpClientFactory.CreateClient(A<Uri>.Ignored))
            .Returns(HttpClientWrapper);
        A.CallTo(() => GitHubClientFactory.CreateClient(A<GitHubCatalogOptions>.Ignored))
            .Returns(GitHubClientWrapper);

        var gitHubOptions = Options.Create(CatalogOptions);
        Catalog = new PublicGitHubCatalog(CkJsonSerializer, HttpClientFactory, GitHubClientFactory, gitHubOptions);
    }

    /// <summary>
    ///     The root catalog fetch goes through GetAsync (not GetStringAsync) so the catalog can
    ///     distinguish a 404 (empty catalog) from network failures (source unreachable, AB#4420).
    /// </summary>
    protected void StubRootCatalog(string json)
    {
        A.CallTo(() => HttpClientWrapper.GetAsync("ck-models/v2/catalog.json", A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    protected static CkModelId CreateTestModelId(string name = "TestModel", string version = "1.0.0")
    {
        return new CkModelId(name, version);
    }

    protected static CkModelIdVersionRange CreateTestVersionRange(string name = "TestModel", string range = "[1.0.0]")
    {
        return new CkModelIdVersionRange(name, range);
    }

    protected static CkCompiledModelRoot CreateTestCompiledModel(string name = "TestModel", string version = "1.0.0")
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(name, version),
            Description = "Test model description"
        };
    }
}

/// <summary>
/// Tests for PublicGitHubCatalog WITHOUT token (uses HTTP Client)
/// </summary>
public class PublicGitHubCatalogWithoutTokenTests : PublicGitHubCatalogTestsBase
{
    public PublicGitHubCatalogWithoutTokenTests() : base(withToken: false)
    {
    }

    [Fact]
    public void Repository_Properties_AreCorrectlySet()
    {
        Assert.Equal(20, Catalog.Order);
        Assert.Equal("PublicGitHubCatalog", Catalog.CatalogName);
        Assert.Equal("Public GitHub catalog", Catalog.Description);
        Assert.True(Catalog.CanWrite);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNull_ReturnsTrue()
    {
        var result = Catalog.IsSupportingSourceIdentifier();
        Assert.True(result);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNonNull_ReturnsFalse()
    {
        var result = Catalog.IsSupportingSourceIdentifier(new object());
        Assert.False(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithEmptySearchTerm_ReturnsEmptyList()
    {
        var result = Catalog.SearchAsync("  ", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithNullSearchTerm_ReturnsEmptyList()
    {
        var result = Catalog.SearchAsync(null!, null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithWhitespaceSearchTerm_ReturnsEmptyList()
    {
        var result = Catalog.SearchAsync("   \t\n  ", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GitHubOptions_WithInvalidPagesUri_ThrowsException(string? pagesUri)
    {
        var invalidOptions = new PublicGitHubCatalogOptions
        {
            GitHubPagesUri = pagesUri!,
            GitHubRepositoryOwner = "testowner",
            GitHubRepositoryName = "testrepo",
            GitHubRepositoryBranch = "main"
        };

        var options = Options.Create(invalidOptions);
        var repository = new PublicGitHubCatalog(CkJsonSerializer, HttpClientFactory, GitHubClientFactory, options);
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        await Assert.ThrowsAsync<ModelCatalogException>(() => repository.GetAsync(modelId, operationResult));
    }

    [Fact]
    public void GitHubOptions_WithoutApiToken_AllowsReadOperations()
    {
        Assert.Null(CatalogOptions.GitHubApiToken);
        Assert.NotNull(Catalog);
    }

    [Fact]
    public async Task PublishAsync_WithoutApiToken_ThrowsException()
    {
        var model = CreateTestCompiledModel();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => Catalog.PublishAsync(model));

        Assert.Contains("GitHub token is missing", exception.Message);
    }

    [Fact]
    public async Task GetAsync_WithValidModelId_UsesHttpClient()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        A.CallTo(() =>
                HttpClientWrapper.GetAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json",
                    A<CancellationToken>.Ignored))
            .Returns(new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => Catalog.GetAsync(modelId, operationResult));

        Assert.Contains("Model 'TestModel-1.0.0' not found in catalog 'PublicGitHubCatalog'", exception.Message);

        // Verify HTTP client was used, NOT GitHub client
        A.CallTo(() => HttpClientWrapper.GetAsync(A<string>._, A<CancellationToken>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task IsExistingAsync_WithValidModelId_UsesHttpClient()
    {
        var modelId = CreateTestModelId();

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1);

        bool result = await Catalog.IsExistingAsync(modelId);

        Assert.True(result);

        // Verify HTTP client was used
        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
    }

    [Fact]
    public async Task RefreshCatalogAsync_UsesHttpClient()
    {
        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog3);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/3/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog3Multiple);

        await Catalog.RefreshCatalogAsync();

        // Verify HTTP client was used
        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public void ListAsync_UsesHttpClient()
    {
        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = Catalog.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Equal(12, result.Count);
        Assert.Equal("TestModel-1.0.0", result.ElementAt(0).ModelId);
        Assert.Equal("TestModel-2.1.2", result.ElementAt(11).ModelId);

        // Verify HTTP client was used
        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_UsesHttpClient()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.5]");

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);

        var result = await Catalog.IsExistingAsync(versionRange);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.5", result.ModelId);

        // Verify HTTP client was used
        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetAsync_WithInvalidRepository_ThrowsException()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        A.CallTo(() => HttpClientWrapper.GetAsync(A<string>._, A<CancellationToken>._))
            .Throws<HttpRequestException>();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Catalog.GetAsync(modelId, operationResult));

        Assert.Contains("is invalid", exception.Message);
    }

    [Fact]
    public async Task RefreshCatalogAsync_WithForce_BypassesFreshCacheWindow()
    {
        StubRootCatalog(Data.RootCatalog);

        // First refresh writes the cache; the second non-forced refresh within the 60s
        // freshness window must be a no-op, a forced refresh must contact the source again.
        await Catalog.RefreshCatalogAsync();
        await Catalog.RefreshCatalogAsync();
        A.CallTo(() => HttpClientWrapper.GetAsync("ck-models/v2/catalog.json", A<CancellationToken>._))
            .MustHaveHappened(1, Times.Exactly);

        await Catalog.RefreshCatalogAsync(null, forceRefresh: true);
        A.CallTo(() => HttpClientWrapper.GetAsync("ck-models/v2/catalog.json", A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task IsExistingAsync_AfterUnreachableRefresh_FlagsSourceUnreachable()
    {
        A.CallTo(() => HttpClientWrapper.GetAsync("ck-models/v2/catalog.json", A<CancellationToken>._))
            .Throws<HttpRequestException>();

        await Catalog.RefreshCatalogAsync(null, forceRefresh: true);
        var result = await Catalog.IsExistingAsync(CreateTestVersionRange(range: "[0.0,)"));

        Assert.False(result.Exists);
        Assert.True(result.SourceUnreachable);
    }

    [Fact]
    public async Task IsExistingAsync_AfterNotFoundRefresh_ReportsEmptyButReachable()
    {
        // A 404 means the source responded and the catalog simply does not exist —
        // this must NOT be flagged as unreachable (first-publication semantics)
        A.CallTo(() => HttpClientWrapper.GetAsync("ck-models/v2/catalog.json", A<CancellationToken>._))
            .ReturnsLazily(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Catalog.RefreshCatalogAsync(null, forceRefresh: true);
        var result = await Catalog.IsExistingAsync(CreateTestVersionRange(range: "[0.0,)"));

        Assert.False(result.Exists);
        Assert.False(result.SourceUnreachable);
    }

    [Fact]
    public async Task GetAsync_WithTimeout_ThrowsException()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        A.CallTo(() => HttpClientWrapper.GetAsync(A<string>._, A<CancellationToken>._))
            .Throws<TaskCanceledException>();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            Catalog.GetAsync(modelId, operationResult));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Tests for PublicGitHubCatalog WITH token (uses GitHub Client)
/// </summary>
public class PublicGitHubCatalogWithTokenTests : PublicGitHubCatalogTestsBase
{
    public PublicGitHubCatalogWithTokenTests() : base(withToken: true)
    {
    }

    [Fact]
    public void Repository_Properties_AreCorrectlySet()
    {
        Assert.Equal(20, Catalog.Order);
        Assert.Equal("PublicGitHubCatalog", Catalog.CatalogName);
        Assert.Equal("Public GitHub catalog", Catalog.Description);
        Assert.True(Catalog.CanWrite);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNull_ReturnsTrue()
    {
        var result = Catalog.IsSupportingSourceIdentifier();
        Assert.True(result);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNonNull_ReturnsFalse()
    {
        var result = Catalog.IsSupportingSourceIdentifier(new object());
        Assert.False(result);
    }

    [Fact]
    public void RepositoryConfiguration_IsProperlyInitialized()
    {
        Assert.Equal("https://test.github.io/repo", CatalogOptions.GitHubPagesUri);
        Assert.Equal("testowner", CatalogOptions.GitHubRepositoryOwner);
        Assert.Equal("testrepo", CatalogOptions.GitHubRepositoryName);
        Assert.Equal("main", CatalogOptions.GitHubRepositoryBranch);
        Assert.Equal("test-token", CatalogOptions.GitHubApiToken);
    }

    // Reads always go through the static gh-pages HTTP path (no GitHub REST quota
    // consumed) regardless of whether the catalog is also configured with a write-token.
    // Token is only used for publish/update mutations.

    [Fact]
    public async Task GetAsync_WithValidModelId_UsesHttpClient()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        A.CallTo(() =>
                HttpClientWrapper.GetAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json",
                    A<CancellationToken>.Ignored))
            .Returns(new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => Catalog.GetAsync(modelId, operationResult));

        Assert.Contains("Model 'TestModel-1.0.0' not found in catalog 'PublicGitHubCatalog'", exception.Message);

        A.CallTo(() => HttpClientWrapper.GetAsync(A<string>._, A<CancellationToken>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task GetAsync_WithExistingModel_ReturnsModel()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();
        var expectedModel = CreateTestCompiledModel();
        var jsonContent = "{\"modelId\":\"TestModel-1.0.0\"}";

        A.CallTo(() =>
                HttpClientWrapper.GetAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json",
                    A<CancellationToken>.Ignored))
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonContent)
            });
        A.CallTo(() => CkJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, A<OperationResult>._, A<bool>._))
            .Returns(expectedModel);

        var result = await Catalog.GetAsync(modelId, operationResult);

        Assert.NotNull(result);

        A.CallTo(() => HttpClientWrapper.GetAsync(A<string>._, A<CancellationToken>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task IsExistingAsync_WithValidModelId_UsesHttpClient()
    {
        var modelId = CreateTestModelId();

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1);

        bool result = await Catalog.IsExistingAsync(modelId);

        Assert.True(result);

        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task RefreshCatalogAsync_UsesHttpClient()
    {
        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog3);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/3/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog3Multiple);

        await Catalog.RefreshCatalogAsync();

        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public void ListAsync_UsesHttpClient()
    {
        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = Catalog.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Equal(12, result.Count);
        Assert.Equal("TestModel-1.0.0", result.ElementAt(0).ModelId);
        Assert.Equal("TestModel-2.1.2", result.ElementAt(11).ModelId);

        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_UsesHttpClient()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.5]");

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);

        var result = await Catalog.IsExistingAsync(versionRange);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.5", result.ModelId);

        A.CallTo(() => HttpClientWrapper.GetStringAsync(A<string>._)).MustHaveHappened();
        A.CallTo(() => GitHubClientWrapper.GetFileAsync(A<string>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task PublishAsync_OK()
    {
        var modelWithDescription = CreateTestCompiledModel();

        A.CallTo(() => CkJsonSerializer.SerializeAsync(A<StreamWriter>._, A<CkCompiledModelRoot>._))
            .Returns(Task.CompletedTask);

        await Catalog.PublishAsync(modelWithDescription);

        // Verify GitHub API calls were made
        A.CallTo(() => GitHubClientWrapper.CreateFileAsync(A<string>._, A<string>._, A<string>._)).MustHaveHappened();
    }

    [Fact]
    public async Task PublishAsync_WithForceFlag_AttemptsToOverwrite()
    {
        var model = CreateTestCompiledModel();

        A.CallTo(() => CkJsonSerializer.SerializeAsync(A<StreamWriter>._, A<CkCompiledModelRoot>._))
            .Returns(Task.CompletedTask);

        A.CallTo(() => GitHubClientWrapper.GetFileAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json"))
            .Returns(("content-test", "sha-test"));

        await Catalog.PublishAsync(model, force: true);

        A.CallTo(() => GitHubClientWrapper.UpdateFileAsync(A<string>._, A<string>._, A<string>._, A<string>._))
            .MustHaveHappened();
    }

    [Fact]
    public async Task PublishAsync_WithCancellationToken_RespectsCancellation()
    {
        var model = CreateTestCompiledModel();
        var cancellationToken = new CancellationToken(true); // Already canceled

        var exception =
            await Assert.ThrowsAnyAsync<Exception>(() =>
                Catalog.PublishAsync(model, cancellationToken: cancellationToken));

        // Should either be a TaskCanceledException or OperationCanceledException
        Assert.True(exception is TaskCanceledException or OperationCanceledException);
    }

    [Fact]
    public async Task GetAsync_WithCancellationToken_RespectsCancellation()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            Catalog.GetAsync(modelId, operationResult, cancellationToken: cancellationToken));

        // Should either be a TaskCanceledException or ModelCatalogException
        Assert.True(exception is TaskCanceledException or ModelCatalogException);
    }

    [Fact]
    public async Task GetModelAsync_WithOperationResultErrors_ThrowsException()
    {
        var modelId = CreateTestModelId();
        var jsonContent = "{\"modelId\":\"TestModel-1.0.0\"}";

        A.CallTo(() =>
                HttpClientWrapper.GetAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json",
                    A<CancellationToken>.Ignored))
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonContent)
            });
        A.CallTo(() => CkJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, A<OperationResult>._, A<bool>._))
            .Invokes((Stream _, string _, OperationResult r, bool _) =>
            {
                r.AddMessage(new OperationMessage(MessageLevel.Error, "TestLocation", 1001, "Test error occurred"));
            })
            .Returns(CreateTestCompiledModel());

        var operationResult = new OperationResult();
        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => Catalog.GetAsync(modelId, operationResult));

        Assert.NotNull(exception);
        Assert.Contains("Error loading model", exception.Message);
    }

    [Theory]
    [InlineData("TestModel", "1.0.0")]
    [InlineData("AnotherModel", "2.1.3")]
    [InlineData("System", "1.0.0")]
    public void ModelId_Creation_WorksCorrectly(string name, string version)
    {
        var modelId = new CkModelId(name, version);
        Assert.Equal(name, modelId.Name);
        Assert.Equal(version, modelId.Version.ToString());
    }

    [Theory]
    [InlineData("TestModel", "[1.0.0]")]
    [InlineData("AnotherModel", "[1.0.0,2.0.0)")]
    [InlineData("System", "1.0.0")]
    public void VersionRange_Creation_WorksCorrectly(string name, string range)
    {
        var versionRange = new CkModelIdVersionRange(name, range);
        Assert.Equal(name, versionRange.Name);
        Assert.NotNull(versionRange);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_FixedVersion_ReturnsCorrectVersion()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.3]");

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);

        var result = await Catalog.IsExistingAsync(versionRange);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.3", result.ModelId);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_BoundedRangeInclusive_ReturnsNewestVersion()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0,2.0]");

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = await Catalog.IsExistingAsync(versionRange);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-2.0.0", result.ModelId);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_OpenRange_ReturnsNewestVersion()
    {
        var versionRange = CreateTestVersionRange(range: "1.0.0");

        StubRootCatalog(Data.RootCatalog);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => HttpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = await Catalog.IsExistingAsync(versionRange);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-2.1.2", result.ModelId);
    }
}
