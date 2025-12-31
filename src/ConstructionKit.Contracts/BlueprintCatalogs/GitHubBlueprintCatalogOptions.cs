using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Defines the GitHub Options for public version of GitHub blueprint catalog
/// </summary>
public class PublicGitHubBlueprintCatalogOptions : GitHubBlueprintCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublicGitHubBlueprintCatalogOptions"/> class.
    /// </summary>
    public PublicGitHubBlueprintCatalogOptions() : base("public-github-blueprint-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "meshmakers.github.io";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/";
    }
}

/// <summary>
/// Defines the GitHub Options for private version of GitHub blueprint catalog
/// </summary>
public class PrivateGitHubBlueprintCatalogOptions : GitHubBlueprintCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrivateGitHubBlueprintCatalogOptions"/> class.
    /// </summary>
    public PrivateGitHubBlueprintCatalogOptions() : base("private-github-blueprint-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "blueprint-libraries-build";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/blueprint-libraries-build/";
    }
}

/// <summary>
/// Defines the GitHub Options for GitHub blueprint catalog
/// </summary>
public abstract class GitHubBlueprintCatalogOptions(string cacheFileName) : BlueprintCatalogOptions(cacheFileName), IGitHubOptions
{
    /// <summary>
    /// API Token for GitHub (optional - only needed for write operations)
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
    /// The uri to the GitHub pages site where the blueprints are stored.
    /// </summary>
    public string GitHubPagesUri { get; set; } = null!;

    /// <summary>
    /// Represents the product name to be used in the User-Agent header when making requests to GitHub API.
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";
}
