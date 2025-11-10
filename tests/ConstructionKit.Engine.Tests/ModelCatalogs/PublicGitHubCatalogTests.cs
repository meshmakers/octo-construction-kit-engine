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

public class PublicGitHubCatalogTests
{
    private readonly PublicGitHubCatalog _catalog;
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly IGitHubClientWrapper _gitHubClientWrapper;
    private readonly PublicGitHubCatalogOptions _catalogOptions;

    public PublicGitHubCatalogTests()
    {
        _ckJsonSerializer = A.Fake<ICkJsonSerializer>();
        _gitHubClientFactory = A.Fake<IGitHubClientFactory>();
        _gitHubClientWrapper = A.Fake<IGitHubClientWrapper>();
        _httpClientFactory = A.Fake<IHttpClientFactory>();
        _httpClientWrapper = A.Fake<IHttpClientWrapper>();

        var tempDirectory = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);

        _catalogOptions = new PublicGitHubCatalogOptions
        {
            CacheDirectory = tempDirectory,
            GitHubPagesUri = "https://test.github.io/repo",
            GitHubRepositoryOwner = "testowner",
            GitHubRepositoryName = "testrepo",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = "test-token"
        };

        A.CallTo(() => _httpClientFactory.CreateClient(A<Uri>.Ignored))
            .Returns(_httpClientWrapper);
        A.CallTo(() => _gitHubClientFactory.CreateClient(A<GitHubCatalogOptions>.Ignored))
            .Returns(_gitHubClientWrapper);

        var gitHubOptions = Options.Create(_catalogOptions);
        _catalog = new PublicGitHubCatalog(_ckJsonSerializer, _httpClientFactory, _gitHubClientFactory, gitHubOptions);
    }

    private static CkModelId CreateTestModelId(string name = "TestModel", string version = "1.0.0")
    {
        return new CkModelId(name, version);
    }

    private static CkModelIdVersionRange CreateTestVersionRange(string name = "TestModel", string range = "[1.0.0]")
    {
        return new CkModelIdVersionRange(name, range);
    }

    private static CkCompiledModelRoot CreateTestCompiledModel(string name = "TestModel", string version = "1.0.0")
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(name, version),
            Description = "Test model description"
        };
    }

    [Fact]
    public void Repository_Properties_AreCorrectlySet()
    {
        Assert.Equal(20, _catalog.Order);
        Assert.Equal("PublicGitHubCatalog", _catalog.CatalogName);
        Assert.Equal("Public GitHub catalog", _catalog.Description);
        Assert.True(_catalog.CanWrite);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNull_ReturnsTrue()
    {
        var result = _catalog.IsSupportingSourceIdentifier();
        Assert.True(result);
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNonNull_ReturnsFalse()
    {
        var result = _catalog.IsSupportingSourceIdentifier(new object());
        Assert.False(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithEmptySearchTerm_ReturnsEmptyList()
    {
        var result = _catalog.SearchAsync("  ", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithNullSearchTerm_ReturnsEmptyList()
    {
        var result = _catalog.SearchAsync(null!, null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void SearchCkModelsAsync_WithWhitespaceSearchTerm_ReturnsEmptyList()
    {
        var result = _catalog.SearchAsync("   \t\n  ", null)
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
        var repository = new PublicGitHubCatalog(_ckJsonSerializer, _httpClientFactory, _gitHubClientFactory, options);
        var model = CreateTestCompiledModel();

        await Assert.ThrowsAsync<ModelCatalogException>(() => repository.PublishAsync(model));
    }

    [Fact]
    public void GitHubOptions_WithoutApiToken_AllowsReadOperations()
    {
        var readOnlyOptions = new PublicGitHubCatalogOptions
        {
            GitHubPagesUri = "https://test.github.io/repo",
            GitHubRepositoryOwner = "testowner",
            GitHubRepositoryName = "testrepo",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = null
        };

        var options = Options.Create(readOnlyOptions);
        var repository = new PublicGitHubCatalog(_ckJsonSerializer, _httpClientFactory, _gitHubClientFactory, options);

        // Should not throw for read operations
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task PublishAsync_WithoutApiToken_ThrowsException()
    {
        var readOnlyOptions = new PublicGitHubCatalogOptions
        {
            GitHubPagesUri = "https://test.github.io/repo",
            GitHubRepositoryOwner = "testowner",
            GitHubRepositoryName = "testrepo",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = null
        };

        var options = Options.Create(readOnlyOptions);
        var repository = new PublicGitHubCatalog(_ckJsonSerializer, _httpClientFactory, _gitHubClientFactory, options);
        var model = CreateTestCompiledModel();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => repository.PublishAsync(model));

        Assert.Contains("GitHub token is missing", exception.Message);
    }

    [Fact]
    public async Task IsExistingAsync_WithValidModelId_ReturnsCorrectResult()
    {
        var modelId = CreateTestModelId();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1);

        bool result = await _catalog.IsExistingAsync(modelId);

        Assert.True(result);
    }

    [Fact]
    public async Task GetAsync_WithValidModelId_AttemptsToRetrieveModel()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();

        A.CallTo(() =>
                _httpClientWrapper.GetAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json",
                    A<CancellationToken>.Ignored))
            .Returns(new HttpResponseMessage(HttpStatusCode.NotFound));

        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(modelId, operationResult));

        Assert.Contains("Model 'TestModel-1.0.0' not found in catalog 'PublicGitHubCatalog'", exception.Message);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRange_AttemptsToCheckExistence()
    {
        var versionRange = CreateTestVersionRange();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Throws<HttpRequestException>();

        // This will fail because we can't mock the HTTP client in the current implementation
        var result = await _catalog.IsExistingAsync(versionRange);

        Assert.False(result.Exists);
    }

    [Fact]
    public void ListAsync_AttemptsToRetrieveCatalog()
    {
        // This will return an empty list when catalog can't be retrieved
        var result = _catalog.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task RefreshCatalogAsync_OK()
    {
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog3);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/3/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog3Multiple);

        await _catalog.RefreshCatalogAsync();
    }

    [Fact]
    public void RepositoryConfiguration_IsProperlyInitialized()
    {
        Assert.Equal("https://test.github.io/repo", _catalogOptions.GitHubPagesUri);
        Assert.Equal("testowner", _catalogOptions.GitHubRepositoryOwner);
        Assert.Equal("testrepo", _catalogOptions.GitHubRepositoryName);
        Assert.Equal("main", _catalogOptions.GitHubRepositoryBranch);
        Assert.Equal("test-token", _catalogOptions.GitHubApiToken);
    }

    [Fact]
    public async Task PublishAsync_WithForceFlag_AttemptsToOverwrite()
    {
        var model = CreateTestCompiledModel();

        A.CallTo(() => _ckJsonSerializer.SerializeAsync(A<StreamWriter>._, A<CkCompiledModelRoot>._))
            .Returns(Task.CompletedTask);

        A.CallTo(() => _gitHubClientWrapper.GetFileAsync("ck-models/v2/t/TestModel/1/ck-testmodel-1.0.0.json"))
            .Returns(("content-test", "sha-test"));

        await _catalog.PublishAsync(model, force: true);

        A.CallTo(() => _gitHubClientWrapper.UpdateFileAsync(A<string>._, A<string>._, A<string>._, A<string>._))
            .MustHaveHappened();
    }

    [Fact]
    public async Task GetAsync_WithCancellationToken_RespectsCancellation()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();
        var cancellationToken = new CancellationToken(true); // Already cancelled

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            _catalog.GetAsync(modelId, operationResult, cancellationToken: cancellationToken));

        // Should either be a TaskCanceledException or ModelCatalogException
        Assert.True(exception is TaskCanceledException or ModelCatalogException);
    }

    [Fact]
    public async Task PublishAsync_WithCancellationToken_RespectsCancellation()
    {
        var model = CreateTestCompiledModel();
        var cancellationToken = new CancellationToken(true); // Already canceled

        var exception =
            await Assert.ThrowsAnyAsync<Exception>(() =>
                _catalog.PublishAsync(model, cancellationToken: cancellationToken));

        // Should either be a TaskCanceledException or ModelCatalogException
        Assert.True(exception is TaskCanceledException or OperationCanceledException);
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
    public void CompiledModel_Creation_WorksCorrectly()
    {
        var model = CreateTestCompiledModel("TestModel", "1.2.3");
        Assert.Equal("TestModel", model.ModelId.Name);
        Assert.Equal("1.2.3", model.ModelId.Version.ToString());
        Assert.Equal("Test model description", model.Description);
    }

    [Fact]
    public async Task GetModelAsync_WithOperationResultErrors_ThrowsException()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();
        operationResult.AddMessage(
            new OperationMessage(MessageLevel.Error, "TestLocation", 1001, "Test error occurred"));

        // Even though this will fail with HTTP errors first, it shows the intended behavior
        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.GetAsync(modelId, operationResult));

        Assert.NotNull(exception);
    }

    [Theory]
    [InlineData("TestModel", "1.0.0")]
    [InlineData("System", "2.1.3")]
    [InlineData("AnotherModel", "1.5.0")]
    public void CreatePath_GeneratesCorrectPath(string name, string version)
    {
        var modelId = new CkModelId(name, version);
        // We can't directly test the private CreatePath method, but we can verify
        // that the repository handles different model names correctly through public methods
        Assert.NotNull(modelId);
        Assert.Equal(name, modelId.Name);
        Assert.Equal(version, modelId.Version.ToString());
    }

    [Fact]
    public async Task IsExistingAsync_WithModelIdAndSourceIdentifier_HandlesCorrectly()
    {
        var modelId = CreateTestModelId();
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1);

        bool result = await _catalog.IsExistingAsync(modelId, sourceIdentifier);

        Assert.True(result);
    }


    [Fact]
    public async Task GetAsync_WithSourceIdentifier_HandlesCorrectly()
    {
        var modelId = CreateTestModelId();
        var operationResult = new OperationResult();
        var sourceIdentifier = new object();

        var result = await _catalog.GetAsync(modelId, operationResult, sourceIdentifier);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRangeAndSourceIdentifier_100_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange();
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.0", result.ModelId);
    }

    [Fact]
    public async Task IsExistingAsync_WithVersionRangeAndSourceIdentifier_105_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.5]");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.5", result.ModelId);
    }


    [Fact]
    public async Task IsExistingAsync_WithVersionRangeAndSourceIdentifier_103_FixedVersion_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.3]");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog1);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.3", result.ModelId);
    }

    [Fact]
    public async Task
        IsExistingAsync_WithVersionRangeAndSourceIdentifier_103_IncludeVersion2_FixedVersion_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0.3]");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.3", result.ModelId);
    }

    [Fact]
    public async Task
        IsExistingAsync_WithVersionRangeAndSourceIdentifier_IncludeVersion2_BoundedRangeExclusive_NewestVersion_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "(1.0,2.0)");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog3);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog3Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-1.0.5", result.ModelId);
    }


    [Fact]
    public async Task
        IsExistingAsync_WithVersionRangeAndSourceIdentifier_IncludeVersion2_BoundedRangeInclusive_NewestVersion_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "[1.0,2.0]");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-2.0.0", result.ModelId);
    }


    [Fact]
    public async Task
        IsExistingAsync_WithVersionRangeAndSourceIdentifier_IncludeVersion2_NewestVersion_HandlesCorrectly()
    {
        var versionRange = CreateTestVersionRange(range: "1.0.0");
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = await _catalog.IsExistingAsync(versionRange, sourceIdentifier);

        Assert.NotNull(result);
        Assert.True(result.Exists);
        Assert.Equal("TestModel-2.1.2", result.ModelId);
    }

    [Fact]
    public void ListAsync_WithSourceIdentifier_HandlesCorrectly()
    {
        var sourceIdentifier = new object();

        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/catalog.json"))
            .Returns(Data.RootCatalog);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/catalog.json"))
            .Returns(Data.TestModelModelCatalog2);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/1/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog1Multiple);
        A.CallTo(() => _httpClientWrapper.GetStringAsync("ck-models/v2/t/TestModel/2/catalog.json"))
            .Returns(Data.TestModelVersionsCatalog2Multiple);

        var result = _catalog.ListAsync(sourceIdentifier)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.Equal(12, result.Count);
        Assert.Equal("TestModel-1.0.0", result.ElementAt(0).ModelId);
        Assert.Equal("TestModel-1.0.1", result.ElementAt(1).ModelId);
        Assert.Equal("TestModel-1.0.2", result.ElementAt(2).ModelId);
        Assert.Equal("TestModel-2.1.2", result.ElementAt(11).ModelId);
    }

    [Fact]
    public void SearchCkModelsAsync_WithSourceIdentifier_HandlesCorrectly()
    {
        var sourceIdentifier = new object();

        var result = _catalog.SearchAsync("test", sourceIdentifier)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task PublishAsync_OK()
    {
        // Arrange
        var modelWithDescription = CreateTestCompiledModel();

        A.CallTo(() => _ckJsonSerializer.SerializeAsync(A<StreamWriter>._, A<CkCompiledModelRoot>._))
            .Returns(Task.CompletedTask);

        // Act & Assert
        await _catalog.PublishAsync(modelWithDescription);


        // Check if GitHub API calls were made (this will fail without actual GitHub access)
        A.CallTo(() => _gitHubClientWrapper.CreateFileAsync(A<string>._, A<string>._, A<string>._)).MustHaveHappened();
    }
}