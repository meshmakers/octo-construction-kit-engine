using System.Diagnostics;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.MsBuildTasks;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#9388: a catalog server that accepts the connection and never answers. Before the fix one request took
///     3 x 100 s (HttpClient's default timeout, two retries) and the refresh treated a timed-out root index like an
///     unreachable source and went on with the next request, so a refresh of the public and the private catalog
///     waited about 20 minutes; CkCompile's 5-minute limit only stopped waiting and left the request running. The
///     tests run against a real local TCP server (<see cref="SilentTcpServer" />) and real HTTP clients.
/// </summary>
public sealed class CatalogRequestTimeoutTests : IDisposable
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan[] NoBackoff = [TimeSpan.Zero, TimeSpan.Zero];

    // Generous bound for "ended quickly": far below the old 307 s per request, tolerant of a loaded CI agent.
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(20);

    private readonly SilentTcpServer _server = new();
    private readonly string _cacheDirectory =
        Path.Combine(Path.GetTempPath(), "CatalogRequestTimeoutTests", Guid.NewGuid().ToString());

    public CatalogRequestTimeoutTests()
    {
        Directory.CreateDirectory(_cacheDirectory);
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, true);
        }
    }

    [Fact]
    public async Task A_request_to_a_server_that_never_answers_times_out_per_attempt_and_is_retried()
    {
        var wrapper = new HttpClientWrapper(_server.BaseUri, ShortTimeout, NoBackoff);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(() =>
            wrapper.GetStringAsync("ck-models/v2/catalog.json", TestContext.Current.CancellationToken));

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
        var timeout = Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Contains($"{_server.BaseUri}/ck-models/v2/catalog.json", timeout.Message);
        Assert.Contains("3 attempt(s)", timeout.Message);
        Assert.Equal(3, _server.AcceptedConnections);
    }

    [Fact]
    public void The_default_request_timeout_is_far_below_HttpClients_100_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), HttpClientWrapper.DefaultRequestTimeout);
    }

    [Fact]
    public async Task The_callers_cancellation_ends_a_hanging_request_at_once_without_retries()
    {
        var wrapper = new HttpClientWrapper(_server.BaseUri);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(ShortTimeout);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            wrapper.GetStringAsync("ck-models/v2/catalog.json", cancellation.Token));

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
        Assert.Equal(1, _server.AcceptedConnections);
    }

    [Fact]
    public async Task A_refresh_against_a_server_that_never_answers_fails_with_catalog_and_url()
    {
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            catalog.RefreshCatalogAsync(null, true, TestContext.Current.CancellationToken));

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
        Assert.True(exception.IsRequestTimeout);
        Assert.Contains("'PrivateGitHubCatalog'", exception.Message);
        Assert.Contains($"{_server.BaseUri}/ck-models/v2/catalog.json", exception.Message);
        // The refresh stopped at the first timed-out request instead of walking on to the next one.
        Assert.Equal(3, _server.AcceptedConnections);
    }

    [Fact]
    public async Task A_reader_that_can_ask_the_repository_flags_the_silent_mirror_instead_of_failing()
    {
        // A publisher (token, API reads) still has the repository as its source of truth (AB#6580): the refresh writes
        // the cache flagged as unreachable, and it does not ask the silent mirror for the v3 root as well.
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff), apiReader: true);
        var stopwatch = Stopwatch.StartNew();

        await catalog.RefreshCatalogAsync(null, true, TestContext.Current.CancellationToken);

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
        Assert.Equal(3, _server.AcceptedConnections);
        Assert.True(ReadSourceUnreachable());
    }

    [Fact]
    public async Task A_canceled_refresh_reads_nothing_and_writes_no_cache()
    {
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff), apiReader: true);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.RefreshCatalogAsync(null, true, canceled.Token));

        Assert.Equal(0, _server.AcceptedConnections);
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_canceled_refresh_of_a_repository_backed_catalog_starts_no_repository_read()
    {
        // Without a Pages URI the refresh reads through the repository (Octokit), whose reads take no token: the
        // refresh has to stop before the first one.
        var gitHubClientFactory = A.Fake<IGitHubClientFactory>();
        var catalog = new PrivateGitHubCatalog(A.Fake<ICkJsonSerializer>(), A.Fake<IHttpClientFactory>(),
            gitHubClientFactory, Options.Create(new PrivateGitHubCatalogOptions
            {
                CacheDirectory = _cacheDirectory,
                GitHubPagesUri = "",
                GitHubRepositoryOwner = "testowner",
                GitHubRepositoryName = "testrepo",
                GitHubRepositoryBranch = "main",
                GitHubApiToken = "test-token",
                ApiReads = CatalogApiReadMode.Enabled,
                RefreshNotFoundRetryCount = 0,
                RefreshNotFoundRetryDelaySeconds = 0
            }));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            catalog.RefreshCatalogAsync(null, true, canceled.Token));

        A.CallTo(gitHubClientFactory).MustNotHaveHappened();
        Assert.Empty(Directory.GetFiles(_cacheDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_reader_that_can_ask_the_repository_flags_a_silent_library_index_instead_of_failing()
    {
        // The mirror answers the v2 root index (two models) and nothing else. A publisher keeps the repository as its
        // source of truth: the refresh flags the cache and does not ask the silent mirror for the second model.
        const string rootIndex =
            "{\"models\":[{\"modelName\":\"A\",\"catalogPath\":\"ck-models/v2/a/A/catalog.json\"}," +
            "{\"modelName\":\"B\",\"catalogPath\":\"ck-models/v2/b/B/catalog.json\"}]}";
        using var server = new SilentTcpServer(path => path.EndsWith("/ck-models/v2/catalog.json") ? rootIndex : null);
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff), apiReader: true,
            pagesUri: server.BaseUri);
        var stopwatch = Stopwatch.StartNew();

        await catalog.RefreshCatalogAsync(null, true, TestContext.Current.CancellationToken);

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
        // root v2 (answered) + v3 root (silent, 3 attempts) or not, then 3 attempts for model A - never model B.
        Assert.True(server.AcceptedConnections <= 7, $"connections: {server.AcceptedConnections}");
        Assert.True(ReadSourceUnreachable());
    }

    [Fact]
    public async Task An_anonymous_reader_fails_on_a_silent_library_index_with_catalog_and_url()
    {
        const string rootIndex =
            "{\"models\":[{\"modelName\":\"A\",\"catalogPath\":\"ck-models/v2/a/A/catalog.json\"}]}";
        using var server = new SilentTcpServer(path =>
            path.EndsWith("/ck-models/v2/catalog.json") ? rootIndex
            : path.EndsWith("/ck-models/v3/catalog.json") ? "{\"models\":[]}" : null);
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff), pagesUri: server.BaseUri);

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() =>
            catalog.RefreshCatalogAsync(null, true, TestContext.Current.CancellationToken));

        Assert.True(exception.IsRequestTimeout);
        Assert.Contains($"{server.BaseUri}/ck-models/v2/a/A/catalog.json", exception.Message);
    }

    [Fact]
    public async Task The_refresh_through_the_catalog_manager_ends_when_its_token_is_canceled()
    {
        // Default request timeout (30 s): only the token can end the refresh this quickly.
        var catalog = CreateCatalog(new HttpClientWrapperFactory(HttpClientWrapper.DefaultRequestTimeout, NoBackoff));
        var manager = new CatalogManager(Microsoft.Extensions.Logging.Abstractions.NullLogger<CatalogManager>.Instance, [catalog]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(ShortTimeout);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.RefreshAllCatalogCachesAsync(null, true, cancellation.Token));

        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void The_task_runner_cancels_a_hanging_refresh_when_the_time_limit_is_reached()
    {
        var catalog = CreateCatalog(new HttpClientWrapperFactory(HttpClientWrapper.DefaultRequestTimeout, NoBackoff));
        var stopwatch = Stopwatch.StartNew();

        var outcome = CatalogTaskRunner.Run(token => catalog.RefreshCatalogAsync(null, true, token),
            ShortTimeout, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(CatalogTaskRunner.Outcome.TimedOut, outcome);
        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void The_task_runner_reports_a_build_cancellation()
    {
        var catalog = CreateCatalog(new HttpClientWrapperFactory(HttpClientWrapper.DefaultRequestTimeout, NoBackoff));
        using var build = new CancellationTokenSource();
        build.CancelAfter(ShortTimeout);

        var outcome = CatalogTaskRunner.Run(token => catalog.RefreshCatalogAsync(null, true, token),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), build.Token);

        Assert.Equal(CatalogTaskRunner.Outcome.Canceled, outcome);
    }

    [Fact]
    public void The_task_runner_gives_up_on_work_that_ignores_its_cancellation()
    {
        var outcome = CatalogTaskRunner.Run(_ => Task.Delay(TimeSpan.FromSeconds(30)),
            ShortTimeout, ShortTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(CatalogTaskRunner.Outcome.Hung, outcome);
    }

    [Fact]
    public void After_an_early_build_cancellation_the_task_runner_waits_only_the_grace_period()
    {
        using var build = new CancellationTokenSource();
        build.CancelAfter(ShortTimeout);
        var stopwatch = Stopwatch.StartNew();

        // Work that ignores its token, a five-minute limit: only the grace period after the cancellation may pass.
        var outcome = CatalogTaskRunner.Run(_ => Task.Delay(TimeSpan.FromSeconds(60)),
            TimeSpan.FromMinutes(5), ShortTimeout, build.Token);

        Assert.Equal(CatalogTaskRunner.Outcome.Hung, outcome);
        Assert.True(stopwatch.Elapsed < Bounded, $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void The_task_runner_passes_other_failures_on()
    {
        var aggregate = Assert.Throws<AggregateException>(() => CatalogTaskRunner.Run(
            _ => throw new InvalidOperationException("boom"), TimeSpan.FromMinutes(1), ShortTimeout,
            TestContext.Current.CancellationToken));

        Assert.IsType<InvalidOperationException>(aggregate.InnerException);
    }

    [Fact]
    public void The_catalog_request_timeout_is_found_in_the_exception_chain_of_the_task()
    {
        var catalog = CreateCatalog(new HttpClientWrapperFactory(ShortTimeout, NoBackoff));
        var aggregate = Assert.Throws<AggregateException>(() => CatalogTaskRunner.Run(
            token => catalog.RefreshCatalogAsync(null, true, token), TimeSpan.FromMinutes(1), ShortTimeout,
            TestContext.Current.CancellationToken));

        var timeout = CatalogTaskRunner.FindCatalogRequestTimeout(aggregate);

        Assert.NotNull(timeout);
        Assert.Contains("'PrivateGitHubCatalog'", timeout.Message);
        Assert.Null(CatalogTaskRunner.FindCatalogRequestTimeout(
            new AggregateException(new TaskCanceledException("other"))));
    }

    private bool ReadSourceUnreachable()
    {
        // AB#9305: the cache directory also holds the writer lock file (.<cache file>.lock); the cache is the JSON file.
        var cacheFile = Assert.Single(Directory.GetFiles(_cacheDirectory, "*.json", SearchOption.AllDirectories));
        using var cache = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cacheFile));
        return cache.RootElement.EnumerateObject()
            .Single(p => string.Equals(p.Name, "SourceUnreachable", StringComparison.OrdinalIgnoreCase)).Value
            .GetBoolean();
    }

    private PrivateGitHubCatalog CreateCatalog(IHttpClientFactory httpClientFactory, bool apiReader = false,
        Uri? pagesUri = null) =>
        new(A.Fake<ICkJsonSerializer>(), httpClientFactory, A.Fake<IGitHubClientFactory>(),
            Options.Create(new PrivateGitHubCatalogOptions
            {
                CacheDirectory = _cacheDirectory,
                GitHubPagesUri = (pagesUri ?? _server.BaseUri).ToString(),
                GitHubRepositoryOwner = "testowner",
                GitHubRepositoryName = "testrepo",
                GitHubRepositoryBranch = "main",
                GitHubApiToken = apiReader ? "test-token" : null,
                ApiReads = apiReader ? CatalogApiReadMode.Enabled : CatalogApiReadMode.Disabled,
                RefreshNotFoundRetryCount = 0,
                RefreshNotFoundRetryDelaySeconds = 0
            }));

    private sealed class HttpClientWrapperFactory(TimeSpan requestTimeout, TimeSpan[] backoffs) : IHttpClientFactory
    {
        public IHttpClientWrapper CreateClient(Uri baseAddress) =>
            new HttpClientWrapper(baseAddress, requestTimeout, backoffs);
    }
}
