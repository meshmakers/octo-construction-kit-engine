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
        GitHubRepositoryName = "construction-kit-libraries";
        GitHubRepositoryBranch = "main";
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
}