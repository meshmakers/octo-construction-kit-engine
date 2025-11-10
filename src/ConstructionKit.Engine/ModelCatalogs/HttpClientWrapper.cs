using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class HttpClientWrapper(Uri baseUri) : IHttpClientWrapper
{
    private readonly HttpClient _client = new() { BaseAddress = baseUri };

    public async Task<string?> GetStringAsync(string? requestUri)
    {
        var uri = baseUri.AbsolutePath.EnsureEndsWith("/") + requestUri;
        var response = await _client.GetAsync(uri, CancellationToken.None).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken)
    {
        var uri = baseUri.AbsolutePath.EnsureEndsWith("/") + requestUri;
        return await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
    }


}