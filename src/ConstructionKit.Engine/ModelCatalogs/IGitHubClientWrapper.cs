namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// What the Contents API can say about the catalog repository itself (AB#6580). A <c>404</c> on a file means "no such file"
/// only when the repository and its branch exist; otherwise it means the catalog cannot be asked at all.
/// </summary>
public enum GitHubRepositoryState
{
    /// <summary>The repository and the configured branch exist.</summary>
    Ready,

    /// <summary>The repository exists and has no commit yet: a catalog that does not hold anything.</summary>
    Empty,

    /// <summary>The repository does not exist, or the token cannot see it.</summary>
    RepositoryNotFound,

    /// <summary>The repository exists and has branches, but not the configured one.</summary>
    BranchNotFound
}

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
    /// Asks GitHub whether the repository and the configured branch exist (AB#6580), so that a <c>null</c> from
    /// <see cref="GetFileAsync" /> can be told apart from an unreachable or misconfigured catalog.
    /// </summary>
    /// <returns>The state of the repository; transport errors, rate limits and server errors are thrown.</returns>
    Task<GitHubRepositoryState> GetRepositoryStateAsync();

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

    /// <summary>
    /// Reads a file, lets the caller merge new content on top of the current state, and writes the
    /// result — retrying the whole read-merge-write cycle on SHA conflicts, so a concurrent
    /// writer's changes are re-read and merged instead of being clobbered. Reads go through the
    /// authenticated GitHub Contents API, which is consistent with the repository (unlike the
    /// gh-pages mirror, which lags behind while a Pages deployment runs).
    /// </summary>
    /// <param name="filePath">File path in the repository.</param>
    /// <param name="commitMessage">The commit message for the create/update.</param>
    /// <param name="merge">Maps the current file content (null when the file does not exist) to
    /// the content to write; returning null skips the write (already up to date).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpsertFileWithMergeAsync(string filePath, string commitMessage, Func<string?, string?> merge);

    /// <summary>
    /// Deletes a file from the GitHub repository.
    /// </summary>
    /// <param name="filePath">File path in the repository.</param>
    /// <param name="commitMessage">The commit message for the deletion.</param>
    /// <param name="sha">The SHA of the file being deleted.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteFileAsync(string filePath, string commitMessage, string sha);

    /// <summary>
    /// Lists all files under a directory in the GitHub repository, recursively.
    /// </summary>
    /// <param name="directoryPath">Directory path in the repository (no trailing slash required).</param>
    /// <returns>The path and SHA of every file under the directory; empty when the directory does not exist.</returns>
    Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath);
}