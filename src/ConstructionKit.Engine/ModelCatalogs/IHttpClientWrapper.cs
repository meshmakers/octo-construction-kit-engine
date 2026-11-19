namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Interface for a wrapper around HttpClient to facilitate testing and abstraction.
/// </summary>
public interface IHttpClientWrapper
{
    /// <summary>
    /// Sends an HTTP GET request and returns the response body as a string asynchronously.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The response body as a string; null when the server answers 404.</returns>
    Task<string?> GetStringAsync(string? requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an HTTP GET request asynchronously.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The response message.</returns>
    Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken);
}