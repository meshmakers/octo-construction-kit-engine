namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Interface for a wrapper around the GitHub client for easier testing.
/// </summary>
public interface IGitHubClientWrapper
{
    /// <summary>
    /// Gets the content and SHA of a file from the GitHub repository.
    /// </summary>
    /// <param name="filePath">File path in the repository.</param>
    /// <returns>A tuple containing the file content and SHA, or null if the file does not exist.</returns>
    Task<(string, string)?> GetFileAsync(string filePath);

    /// <summary>
    /// Updates a file in the GitHub repository.
    /// </summary>
    /// <param name="filePath">File path in the repository.</param>
    /// <param name="commitMessage">The commit message for the update.</param>
    /// <param name="content">The new content for the file.</param>
    /// <param name="sha">The SHA of the file being replaced.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha);

    /// <summary>
    /// Creates a new file in the GitHub repository.
    /// </summary>
    /// <param name="filePath">File path in the repository.</param>
    /// <param name="commitMessage">The commit message for the update.</param>
    /// <param name="content">The new content for the file.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateFileAsync(string filePath, string commitMessage, string content);
}