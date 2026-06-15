using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class HttpClientWrapper(Uri baseUri) : IHttpClientWrapper
{
    private readonly HttpClient _client = new() { BaseAddress = baseUri };

    // Retry settings for transient GitHub-catalog failures. Build #35172 (r3.4.13)
    // hit a one-off `TaskCanceledException` against api.github.com during a CK
    // compile and canceled the whole CK build via CkCompile's 5-minute task
    // timeout, propagating up as a services-train cancel. The flake was gone on a
    // manual re-run a few seconds later. Three attempts (initial + 2 retries)
    // with a short fixed-then-exponential backoff turns that class of one-off
    // network glitches into a non-event without masking real outages.
    private const int MaxRetries = 2;
    private static readonly TimeSpan[] RetryBackoffs =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    public async Task<string?> GetStringAsync(string? requestUri)
    {
        var uri = baseUri.AbsolutePath.EnsureEndsWith("/") + requestUri;
        var response = await SendWithRetryAsync(uri, CancellationToken.None).ConfigureAwait(false);
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
        return await SendWithRetryAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    // Single GET with retry on transient failures. Conditions worth retrying:
    //   - HttpRequestException (DNS, TCP reset, TLS handshake glitch)
    //   - TaskCanceledException with the caller's token NOT canceled (= our own
    //     HttpClient timeout fired — distinguish from a real upstream cancel)
    //   - 5xx server errors and 408 / 429 (transient throttling)
    // Non-transient (404, 4xx other than 408/429) returns immediately so the
    // caller's existing semantics — e.g. GetStringAsync mapping 404→null —
    // keep working.
    private async Task<HttpResponseMessage> SendWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!IsTransientStatus(response.StatusCode) || attempt == MaxRetries)
                {
                    return response;
                }
                // Drain + dispose the transient response before backing off — leaving
                // it open holds the connection pool slot until GC kicks in, and on
                // a constrained CI agent the next retry can stall waiting for a slot.
                response.Dispose();
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt == MaxRetries) throw;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Our own HttpClient timeout, not a caller-initiated cancel.
                lastException = ex;
                if (attempt == MaxRetries) throw;
            }

            await Task.Delay(RetryBackoffs[attempt], cancellationToken).ConfigureAwait(false);
        }

        // Unreachable in practice — last attempt either returns or rethrows above.
        // Keep the safety net so the compiler is happy.
        throw lastException ?? new HttpRequestException("Retry loop exhausted without a captured exception.");
    }

    private static bool IsTransientStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.RequestTimeout            // 408
        || (int)statusCode == 429                                         // TooManyRequests (named enum missing on netstandard2.0)
        || (int)statusCode >= 500;                                        // 5xx
}