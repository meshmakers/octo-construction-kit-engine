using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Interface for a factory that creates GitHub client instances.
/// </summary>
public interface IGitHubClientFactory
{
    /// <summary>
    /// Gets a GitHub client instance configured with the specified product name and authentication token.
    /// </summary>
    /// <param name="gitHubOptions">The GitHub options containing the product name and authentication token.</param>
    /// <returns>An instance of <see cref="IGitHubClientWrapper"/>.</returns>
    IGitHubClientWrapper CreateClient(IGitHubOptions gitHubOptions);
}