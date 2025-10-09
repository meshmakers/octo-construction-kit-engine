namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class HttpClientWrapper(Uri baseUri) : IHttpClientWrapper
{
    private readonly HttpClient _client = new() { BaseAddress = baseUri };

    public async Task<string?> GetStringAsync(string? requestUri)
    {
        var response = await _client.GetAsync(requestUri, CancellationToken.None).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken)
    {
        return await _client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        return await _client.SendAsync(request).ConfigureAwait(false);
    }
}