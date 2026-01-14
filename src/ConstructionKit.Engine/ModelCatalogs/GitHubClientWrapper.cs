using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientWrapper(IGitHubOptions gitHubOptions) : IGitHubClientWrapper
{
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
        await _client.Repository.Content.UpdateFile(
            gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
            new UpdateFileRequest(commitMessage, content,
                sha)).ConfigureAwait(false);
    }

    public async Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        await _client.Repository.Content.CreateFile(
            gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
            new CreateFileRequest(commitMessage, content)).ConfigureAwait(false);
    }
}