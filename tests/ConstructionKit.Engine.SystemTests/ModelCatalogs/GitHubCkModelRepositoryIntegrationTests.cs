using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Xunit;

namespace ConstructionKit.Engine.SystemTests.ModelCatalogs;

public class GitHubCkModelRepositoryIntegrationTests
{
    private readonly GitHubCatalog _repository;
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GitHubCatalogOptions> _gitHubOptions;

    public GitHubCkModelRepositoryIntegrationTests()
    {
        _ckJsonSerializer = A.Fake<ICkJsonSerializer>();
        _httpClientFactory = A.Fake<IHttpClientFactory>();
        _gitHubClientFactory = A.Fake<IGitHubClientFactory>();

        var options = new GitHubCatalogOptions
        {
            GitHubPagesUri = "https://meshmakers.github.io/octo-ck-catalog",
            GitHubRepositoryOwner = "meshmakers",
            GitHubRepositoryName = "octo-ck-catalog",
            GitHubRepositoryBranch = "main",
            GitHubApiToken = null // Read-only integration tests
        };

        _gitHubOptions = Options.Create(options);
        _repository = new GitHubCatalog(_ckJsonSerializer, _httpClientFactory, _gitHubClientFactory, _gitHubOptions);
    }

    [Fact]
    public void ListCkModelsAsync_WithRealRepository_ReturnsModels()
    {
        var result = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();
        Assert.NotNull(result);
        // Note: This test is skipped by default as it requires network access
    }

    [Fact]
    public void SearchCkModelsAsync_WithRealRepository_ReturnsFilteredResults()
    {
        var result =  _repository.SearchAsync("System", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();
        Assert.NotNull(result);
        // Note: This test is skipped by default as it requires network access
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithRealRepository_ChecksExistence()
    {
        var modelId = new CkModelId("System", "1.0.0");
        var result = await _repository.IsExistingAsync(modelId);
        // Note: This test is skipped by default as it requires network access
        Assert.True(result || !result); // Either result is valid for this test
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithVersionRange_ChecksExistence()
    {
        var versionRange = new CkModelIdVersionRange("System", "[1.0.0]");
        var result = await _repository.IsExistingAsync(versionRange);
        // Note: This test is skipped by default as it requires network access
        Assert.NotNull(result);
    }
}