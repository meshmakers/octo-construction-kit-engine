using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientFactory : IGitHubClientFactory
{
    public IGitHubClientWrapper CreateClient(IGitHubOptions gitHubOptions)
    {
        if (string.IsNullOrWhiteSpace(gitHubOptions.GitHubApiToken) ||
            gitHubOptions.GitHubApiToken == null)
        {
            throw ModelCatalogException.GitHubTokenMissing();
        }

        return new GitHubClientWrapper(gitHubOptions);
    }
}