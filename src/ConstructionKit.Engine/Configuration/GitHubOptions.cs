namespace Meshmakers.Octo.ConstructionKit.Engine.Configuration;

/// <summary>
/// Defines the GitHub Options for GitHub construction kit repository
/// </summary>
public class GitHubOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOptions"/> class.
    /// </summary>
    public GitHubOptions()
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
    public string? GitHubPagesUri { get; set; }
}