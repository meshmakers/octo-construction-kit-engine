namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Interface for a factory that creates HTTP clients.
/// </summary>
public interface IHttpClientFactory
{
    /// <summary>
    /// Creates a new HTTP client with the specified base address.
    /// </summary>
    /// <param name="baseAddress">The base address for the HTTP client.</param>
    /// <returns>An instance of <see cref="IHttpClientWrapper"/>.</returns>
    IHttpClientWrapper CreateClient(Uri baseAddress);
}