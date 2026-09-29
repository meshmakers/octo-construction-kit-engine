using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.MsBuildTasks;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

/// <summary>
///     Covers the lane-scoping of the private CK catalog in the MSBuild tasks (AB#5412). The tasks
///     themselves cannot be exercised without an MSBuild host, but the decision they delegate — which
///     repository a model is resolved from and published to — is a pure function and is worth guarding:
///     getting it wrong means publishing unreleased lane content into the main-lane catalog.
/// </summary>
public class PrivateGitHubCatalogCoordinatesTests
{
    private const string DefaultRepository = "construction-kit-libraries-build";

    [Fact]
    public void Apply_WithoutCoordinates_KeepsCompiledInDefaults()
    {
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, null, string.Empty, "   ", null);

        Assert.Equal("meshmakers", options.GitHubRepositoryOwner);
        Assert.Equal(DefaultRepository, options.GitHubRepositoryName);
        Assert.Equal("main", options.GitHubRepositoryBranch);
        Assert.Equal($"https://meshmakers.github.io/{DefaultRepository}/", options.GitHubPagesUri);
    }

    [Fact]
    public void Apply_WithLaneCoordinates_RetargetsEveryCoordinate()
    {
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, "meshmakers", "octo-catalog-dev", "main",
            "https://meshmakers.github.io/octo-catalog-dev/");

        Assert.Equal("octo-catalog-dev", options.GitHubRepositoryName);
        Assert.Equal("https://meshmakers.github.io/octo-catalog-dev/", options.GitHubPagesUri);
    }

    [Fact]
    public void Apply_TrimsSurroundingWhitespace()
    {
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, " meshmakers ", " octo-catalog-dev ", " main ", null);

        Assert.Equal("meshmakers", options.GitHubRepositoryOwner);
        Assert.Equal("octo-catalog-dev", options.GitHubRepositoryName);
        Assert.Equal("main", options.GitHubRepositoryBranch);
    }

    [Fact]
    public void Apply_PreservesCase()
    {
        // The CLI Config command lower-cases; this must not, because the Pages value is a URL path.
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, null, null, "Release/0.2",
            "https://meshmakers.github.io/Octo-Catalog-Dev/");

        Assert.Equal("Release/0.2", options.GitHubRepositoryBranch);
        Assert.Equal("https://meshmakers.github.io/Octo-Catalog-Dev/", options.GitHubPagesUri);
    }

    [Fact]
    public void Apply_PartialCoordinates_MergeWithDefaults()
    {
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, null, null, "test/0.2-dev", null);

        Assert.Equal("test/0.2-dev", options.GitHubRepositoryBranch);
        Assert.Equal(DefaultRepository, options.GitHubRepositoryName);
    }

    [Theory]
    [InlineData("$(OctoPrivateGitHubCatalogOwner)", null, null, null, "owner")]
    [InlineData(null, "$(OctoPrivateGitHubCatalogRepositoryName)", null, null, "repositoryName")]
    [InlineData(null, null, "$(OctoPrivateGitHubCatalogBranch)", null, "branch")]
    [InlineData(null, null, null, "$(OctoPrivateGitHubCatalogPagesUri)", "pagesUri")]
    public void Validate_RejectsUnexpandedPipelineMacro(string? owner, string? repositoryName, string? branch,
        string? pagesUri, string expectedCoordinate)
    {
        var isValid = PrivateGitHubCatalogCoordinates.Validate(owner, repositoryName, branch, pagesUri,
            out var error);

        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains(expectedCoordinate, error);
    }

    [Fact]
    public void Validate_AcceptsEmptyAndRealCoordinates()
    {
        Assert.True(PrivateGitHubCatalogCoordinates.Validate(null, null, null, null, out var noError));
        Assert.Null(noError);

        Assert.True(PrivateGitHubCatalogCoordinates.Validate("meshmakers", "octo-catalog-dev", "main",
            "https://meshmakers.github.io/octo-catalog-dev/", out var stillNoError));
        Assert.Null(stillNoError);
    }

    [Fact]
    public void Apply_WithoutCoordinates_KeepsTheHistoricalCacheFileName()
    {
        // Must stay byte-identical for main / r-tags: a new name would silently invalidate every
        // existing cache on every machine.
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, null, null, null, null);

        Assert.Equal("private-github-catalog-cache.json", options.CacheFileName);
    }

    [Fact]
    public void Apply_WithLaneCoordinates_ScopesTheCacheFileToTheRepository()
    {
        // Two repositories under one catalog NAME shared one cache file, and a cache inside its age
        // window is served without contacting GitHub — so a retargeted build could resolve the other
        // lane's models (observed: System.Communication 3.36.0, which only the main-lane catalog has).
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, "meshmakers", "octo-catalog-dev", "main",
            "https://meshmakers.github.io/octo-catalog-dev/");

        Assert.Equal("private-github-catalog-cache-meshmakers-octo-catalog-dev-main.json",
            options.CacheFileName);
    }

    [Fact]
    public void Apply_WithSlashInBranch_ProducesAUsableFileName()
    {
        var options = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(options, null, "octo-catalog-dev", "test/0.2-dev", null);

        Assert.Equal("private-github-catalog-cache-meshmakers-octo-catalog-dev-test-0.2-dev.json",
            options.CacheFileName);
        Assert.DoesNotContain("/", options.CacheFileName);
    }

    [Fact]
    public void Apply_DifferentRepositories_NeverShareACacheFile()
    {
        var lane = new PrivateGitHubCatalogOptions();
        var other = new PrivateGitHubCatalogOptions();

        PrivateGitHubCatalogCoordinates.Apply(lane, "meshmakers", "octo-catalog-dev", "main", null);
        PrivateGitHubCatalogCoordinates.Apply(other, "meshmakers", "construction-kit-libraries-build", "main",
            null);

        Assert.NotEqual(lane.CacheFileName, other.CacheFileName);
    }

    [Fact]
    public void Describe_NamesRepositoryAndBranch()
    {
        var options = new PrivateGitHubCatalogOptions();
        PrivateGitHubCatalogCoordinates.Apply(options, "meshmakers", "octo-catalog-dev", "main",
            "https://meshmakers.github.io/octo-catalog-dev/");

        var description = PrivateGitHubCatalogCoordinates.Describe(options);

        Assert.Equal(
            "meshmakers/octo-catalog-dev@main (pages: https://meshmakers.github.io/octo-catalog-dev/)",
            description);
    }
}
