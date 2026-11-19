using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class HttpClientWrapper : IHttpClientWrapper
{
    /// <summary>
    ///     Time one attempt of a catalog request may take, response body included (AB#9388). Catalog index files are a
    ///     few KB and model files a few MB: a healthy GitHub Pages answer takes well under a second.
    /// </summary>
    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    // Retry settings for transient GitHub-catalog failures. Build #35172 (r3.4.13)
    // hit a one-off `TaskCanceledException` against api.github.com during a CK
    // compile and canceled the whole CK build via CkCompile's 5-minute task
    // timeout, propagating up as a services-train cancel. The flake was gone on a
    // manual re-run a few seconds later. Three attempts (initial + 2 retries)
    // with a short fixed-then-exponential backoff turns that class of one-off
    // network glitches into a non-event without masking real outages.
    private static readonly TimeSpan[] DefaultRetryBackoffs =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private readonly Uri _baseUri;
    private readonly TimeSpan _requestTimeout;
    private readonly IReadOnlyList<TimeSpan> _retryBackoffs;

    // The timeout is applied per attempt with a CancellationTokenSource of our own (below), so the client's own
    // timeout is off: it would otherwise cover all attempts of one call together with its default of 100 s.
    private readonly HttpClient _client;

    public HttpClientWrapper(Uri baseUri) : this(baseUri, DefaultRequestTimeout, DefaultRetryBackoffs)
    {
    }

    /// <summary>
    ///     Creates the wrapper with its own per-attempt timeout and retry backoffs (one backoff per retry).
    /// </summary>
    internal HttpClientWrapper(Uri baseUri, TimeSpan requestTimeout, IReadOnlyList<TimeSpan> retryBackoffs)
    {
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), requestTimeout,
                "The request timeout must be positive.");
        }

        _baseUri = baseUri;
        _requestTimeout = requestTimeout;
        _retryBackoffs = retryBackoffs;
        _client = new HttpClient { BaseAddress = baseUri, Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<string?> GetStringAsync(string? requestUri, CancellationToken cancellationToken = default)
    {
        var uri = _baseUri.AbsolutePath.EnsureEndsWith("/") + requestUri;
        using var response = await SendWithRetryAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        // The content was buffered inside the attempt (HttpCompletionOption.ResponseContentRead), under its timeout.
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken)
    {
        var uri = _baseUri.AbsolutePath.EnsureEndsWith("/") + requestUri;
        return await SendWithRetryAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    // Single GET with retry on transient failures. Conditions worth retrying:
    //   - HttpRequestException (DNS, TCP reset, TLS handshake glitch)
    //   - the attempt's own timeout (a server that accepts the connection and never answers, AB#9388) - told apart
    //     from the caller's cancellation, which ends the call at once
    //   - 5xx server errors and 408 / 429 (transient throttling)
    // Non-transient (404, 4xx other than 408/429) returns immediately so the
    // caller's existing semantics — e.g. GetStringAsync mapping 404→null —
    // keep working.
    // When every attempt timed out, the call throws a TaskCanceledException (as HttpClient does for its own timeout)
    // whose inner TimeoutException names the URL, the timeout and the number of attempts.
    private async Task<HttpResponseMessage> SendWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        var maxRetries = _retryBackoffs.Count;
        for (var attempt = 0; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(_requestTimeout);
            try
            {
                var response = await _client.GetAsync(uri, attemptTimeout.Token).ConfigureAwait(false);
                if (!IsTransientStatus(response.StatusCode) || attempt == maxRetries)
                {
                    return response;
                }
                // Drain + dispose the transient response before backing off — leaving
                // it open holds the connection pool slot until GC kicks in, and on
                // a constrained CI agent the next retry can stall waiting for a slot.
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                // retried below
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The attempt's own timeout, not a caller-initiated cancel.
                if (attempt == maxRetries)
                {
                    var timeout = new TimeoutException(
                        $"GET {new Uri(_baseUri, uri)} got no complete response within " +
                        $"{_requestTimeout.TotalSeconds:0.###} s in each of {attempt + 1} attempt(s).", ex);
                    throw new TaskCanceledException(timeout.Message, timeout);
                }
            }

            await Task.Delay(_retryBackoffs[attempt], cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientStatus(System.Net.HttpStatusCode statusCode) =>
        statusCode == System.Net.HttpStatusCode.RequestTimeout            // 408
        || (int)statusCode == 429                                         // TooManyRequests (named enum missing on netstandard2.0)
        || (int)statusCode >= 500;                                        // 5xx
}
