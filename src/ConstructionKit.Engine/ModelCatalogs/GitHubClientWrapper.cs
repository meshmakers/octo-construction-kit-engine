using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientWrapper(IGitHubOptions gitHubOptions) : IGitHubClientWrapper
{
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 500;

    private readonly GitHubClient _client = new(new ProductHeaderValue(gitHubOptions.ProductName))
    {
        Credentials = new Credentials(gitHubOptions.GitHubApiToken)
    };

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

    private static bool IsShaConflict(ApiException ex)
    {
        return ex.Message.Contains("but expected", StringComparison.OrdinalIgnoreCase)
               || ex.StatusCode == System.Net.HttpStatusCode.Conflict;
    }
}
