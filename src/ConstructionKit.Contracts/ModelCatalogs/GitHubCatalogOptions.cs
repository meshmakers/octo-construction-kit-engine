namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
/// Defines the GitHub Options for GitHub construction kit repository
/// </summary>
public class GitHubCatalogOptions : CatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubCatalogOptions"/> class.
    /// </summary>
    public GitHubCatalogOptions() : base("github-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "meshmakers.github.io";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/";
    }

    /// <summary>
    /// API Token for GitHub
    /// </summary>
    public string? GitHubApiToken { get; set; }
    
    /// <summary>
    /// Name of the GitHub repository
    /// </summary>
    public string GitHubRepositoryName { get; set; }
    
    /// <summary>
    /// Owner of the GitHub repository
    /// </summary>
    public string GitHubRepositoryOwner { get; set; }
    
    /// <summary>
    /// GitHub repository branch
    /// </summary>
    public string GitHubRepositoryBranch { get; set; }

    /// <summary>
    /// The uri to the GitHub pages site where the construction kit models are stored.
    /// If null or empty, the repository will use GitHub API for reading models.
    /// </summary>
    public string GitHubPagesUri { get; set; }

    /// <summary>
    /// Represents the product name to be used in the User-Agent header when making requests to GitHub API.
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";
}