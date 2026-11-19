namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     An <see cref="IHttpClientWrapper" /> whose string reads can be canceled (AB#9388). A separate interface, so
///     <see cref="IHttpClientWrapper" /> and its implementations keep their members unchanged.
/// </summary>
public interface ICancellableHttpClientWrapper : IHttpClientWrapper
{
    /// <summary>
    ///     Sends an HTTP GET request and returns the response body as a string asynchronously.
    /// </summary>
    /// <param name="requestUri">The request URI.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The response body as a string; null when the server answers 404.</returns>
    Task<string?> GetStringAsync(string? requestUri, CancellationToken cancellationToken);
}
