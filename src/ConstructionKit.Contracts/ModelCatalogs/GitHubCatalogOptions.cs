using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
/// Defines the GitHub Options for public version of GitHub construction kit catalog
/// </summary>
public class PublicGitHubCatalogOptions : GitHubCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublicGitHubCatalogOptions"/> class.
    /// </summary>
    public PublicGitHubCatalogOptions() : base("public-github-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "meshmakers.github.io";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/";
    }
}

/// <summary>
/// Defines the GitHub Options for private version of GitHub construction kit catalog
/// </summary>
public class PrivateGitHubCatalogOptions : GitHubCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublicGitHubCatalogOptions"/> class.
    /// </summary>
    public PrivateGitHubCatalogOptions() : base("private-github-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "construction-kit-libraries-build";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/construction-kit-libraries-build/";
        IsEnabled = false;
    }
}

/// <summary>
/// Defines the GitHub Options for GitHub construction kit catalog
/// </summary>
public abstract class GitHubCatalogOptions(string cacheFileName) : CatalogOptions(cacheFileName), IGitHubOptions
{
    /// <summary>
    /// API Token for GitHub
    /// </summary>
    public string? GitHubApiToken { get; set; }

    /// <summary>
    /// Name of the GitHub repository
    /// </summary>
    public string GitHubRepositoryName { get; set; } = null!;

    /// <summary>
    /// Owner of the GitHub repository
    /// </summary>
    public string GitHubRepositoryOwner { get; set; } = null!;

    /// <summary>
    /// GitHub repository branch
    /// </summary>
    public string GitHubRepositoryBranch { get; set; } = null!;

    /// <summary>
    /// The uri to the GitHub pages site where the construction kit models are stored.
    /// If null or empty, the repository will use GitHub API for reading models.
    /// </summary>
    public string GitHubPagesUri { get; set; } = null!;

    /// <summary>
    /// Represents the product name to be used in the User-Agent header when making requests to GitHub API.
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";

    /// <summary>
    /// When true, this GitHub catalog is enabled for reading and writing models.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}