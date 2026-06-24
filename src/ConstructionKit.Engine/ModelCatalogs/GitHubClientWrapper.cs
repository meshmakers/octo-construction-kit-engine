using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientWrapper : IGitHubClientWrapper
{
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 500;

    // Octokit's underlying HttpClient defaults to 100 s per request. That is too long for build
    // pipelines: when GitHub's API stalls or rate-limits, a single stuck call freezes CkCompile /
    // ckc publish for the full timeout and bubbles up as a cryptic "A task was canceled". 60 s is
    // generous for healthy paginated reads but fails fast on real hangs.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly IGitHubOptions gitHubOptions;
    private readonly GitHubClient _client;

    public GitHubClientWrapper(IGitHubOptions gitHubOptions)
    {
        this.gitHubOptions = gitHubOptions;
        _client = new GitHubClient(new ProductHeaderValue(gitHubOptions.ProductName))
        {
            Credentials = new Credentials(gitHubOptions.GitHubApiToken)
        };
        _client.SetRequestTimeout(RequestTimeout);
    }

    public async Task<(string, string)?> GetFileAsync(string filePath)
    {
        try
        {
            // Currently there is no way to check if a file exists without trying to get it and catching the exception
            var file = await _client.Repository.Content.GetAllContentsByRef(
                gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                gitHubOptions.GitHubRepositoryBranch).ConfigureAwait(false);
            if (file.Count == 0)
            {
                return null;
            }
            return (file.First().Content, file.First().Sha);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    public async Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha)
    {
        var currentSha = sha;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.UpdateFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new UpdateFileRequest(commitMessage, content, currentSha)).ConfigureAwait(false);
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var refreshed = await GetFileAsync(filePath).ConfigureAwait(false);
                if (!refreshed.HasValue)
                {
                    throw;
                }
                currentSha = refreshed.Value.Item2;
            }
        }
    }

    public async Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.CreateFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new CreateFileRequest(commitMessage, content)).ConfigureAwait(false);
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                // File may have been created by a parallel build — try updating instead
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var existing = await GetFileAsync(filePath).ConfigureAwait(false);
                if (existing.HasValue)
                {
                    await UpdateFileAsync(filePath, commitMessage, content, existing.Value.Item2)
                        .ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    public async Task DeleteFileAsync(string filePath, string commitMessage, string sha)
    {
        var currentSha = sha;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.DeleteFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new DeleteFileRequest(commitMessage, currentSha, gitHubOptions.GitHubRepositoryBranch))
                    .ConfigureAwait(false);
                return;
            }
            catch (NotFoundException)
            {
                // Already gone — deletion is idempotent.
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var refreshed = await GetFileAsync(filePath).ConfigureAwait(false);
                if (!refreshed.HasValue)
                {
                    // File disappeared between attempts — nothing left to delete.
                    return;
                }
                currentSha = refreshed.Value.Item2;
            }
        }
    }

    public async Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath)
    {
        var prefix = directoryPath.TrimEnd('/') + "/";

        try
        {
            // One recursive tree read for the branch, then filter to blobs under the directory prefix.
            // The blob SHA returned here is exactly what DeleteFile requires.
            var tree = await _client.Git.Tree.GetRecursive(
                gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName,
                gitHubOptions.GitHubRepositoryBranch).ConfigureAwait(false);

            if (tree.Truncated)
            {
                // The Git Trees API silently truncates very large trees. A partial list would make callers
                // (e.g. blueprint unpublish) delete only some files and leave orphans with no error, so fail
                // loudly instead of returning a partial result.
                throw new InvalidOperationException(
                    $"GitHub returned a truncated tree for '{gitHubOptions.GitHubRepositoryName}'; cannot safely enumerate files under '{directoryPath}'.");
            }

            return tree.Tree
                .Where(item => item.Type == TreeType.Blob &&
                               item.Path.StartsWith(prefix, StringComparison.Ordinal))
                .Select(item => (item.Path, item.Sha))
                .ToList();
        }
        catch (NotFoundException)
        {
            return [];
        }
    }

    private static bool IsShaConflict(ApiException ex)
    {
        return ex.Message.Contains("but expected", StringComparison.OrdinalIgnoreCase)
               || ex.StatusCode == System.Net.HttpStatusCode.Conflict;
    }
}
