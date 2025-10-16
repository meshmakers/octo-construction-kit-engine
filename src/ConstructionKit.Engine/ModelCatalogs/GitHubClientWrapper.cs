using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientWrapper(GitHubCatalogOptions gitHubCatalogOptions) : IGitHubClientWrapper
{
    private readonly GitHubClient _client = new(new ProductHeaderValue(gitHubCatalogOptions.ProductName))
    {
        Credentials = new Credentials(gitHubCatalogOptions.GitHubApiToken)
    };

    public async Task<(string, string)?> GetFileAsync(string filePath)
    {
        try
        {
            // Currently there is no way to check if a file exists without trying to get it and catching the exception
            var file = await _client.Repository.Content.GetAllContentsByRef(
                gitHubCatalogOptions.GitHubRepositoryOwner, gitHubCatalogOptions.GitHubRepositoryName, filePath,
                gitHubCatalogOptions.GitHubRepositoryBranch).ConfigureAwait(false);
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
            gitHubCatalogOptions.GitHubRepositoryOwner, gitHubCatalogOptions.GitHubRepositoryName, filePath,
            new UpdateFileRequest(commitMessage, content,
                sha)).ConfigureAwait(false);
    }

    public async Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        await _client.Repository.Content.CreateFile(
            gitHubCatalogOptions.GitHubRepositoryOwner, gitHubCatalogOptions.GitHubRepositoryName, filePath,
            new CreateFileRequest(commitMessage, content)).ConfigureAwait(false);
    }
}