using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientFactory : IGitHubClientFactory
{
    public IGitHubClientWrapper CreateClient(GitHubCatalogOptions gitHubCatalogOptions)
    {
        if (string.IsNullOrWhiteSpace(gitHubCatalogOptions.GitHubApiToken) ||
            gitHubCatalogOptions.GitHubApiToken == null)
        {
            throw ModelCatalogException.GitHubTokenMissing();
        }

        return new GitHubClientWrapper(gitHubCatalogOptions);
    }
}