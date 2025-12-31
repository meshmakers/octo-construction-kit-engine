namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Common interface for GitHub catalog options.
/// </summary>
public interface IGitHubOptions
{
    /// <summary>
    /// API Token for GitHub
    /// </summary>
    string? GitHubApiToken { get; set; }

    /// <summary>
    /// Name of the GitHub repository
    /// </summary>
    string GitHubRepositoryName { get; set; }

    /// <summary>
    /// Owner of the GitHub repository
    /// </summary>
    string GitHubRepositoryOwner { get; set; }

    /// <summary>
    /// GitHub repository branch
    /// </summary>
    string GitHubRepositoryBranch { get; set; }

    /// <summary>
    /// The uri to the GitHub pages site.
    /// </summary>
    string GitHubPagesUri { get; set; }

    /// <summary>
    /// Represents the product name to be used in the User-Agent header when making requests to GitHub API.
    /// </summary>
    string ProductName { get; set; }
}
