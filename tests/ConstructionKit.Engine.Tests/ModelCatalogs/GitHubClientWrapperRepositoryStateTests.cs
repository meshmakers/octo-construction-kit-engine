using System.Net;
using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#6580: the REAL <see cref="GitHubClientWrapper" /> (Octokit included) against a local HTTP stub of the GitHub REST
///     API. A <c>404</c> on a file is "no such file" only in a repository that exists with its branch; the tests pin what
///     the wrapper says for each answer of the API, so the catalog can tell a genuinely new catalog from a missing one.
/// </summary>
public sealed class GitHubClientWrapperRepositoryStateTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, (int Status, string Body)> _routes = new();
    private readonly Uri _baseAddress;
    private readonly CancellationTokenSource _stop = new();

    public GitHubClientWrapperRepositoryStateTests()
    {
        HttpListenerException? last = null;
        Uri? address = null;
        for (var attempt = 0; attempt < 20 && address == null; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                address = new Uri($"http://127.0.0.1:{port}/");
            }
            catch (HttpListenerException e)
            {
                last = e;
            }
        }

        _baseAddress = address ?? throw new InvalidOperationException("no free port", last);
        _ = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            var path = context.Request.Url!.AbsolutePath;
            var (status, body) = _routes.TryGetValue(path, out var route) ? route : (404, "{\"message\":\"Not Found\"}");
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private GitHubClientWrapper Wrapper() => new(new PrivateGitHubCatalogOptions
    {
        GitHubApiToken = "test-token", GitHubRepositoryOwner = "o", GitHubRepositoryName = "r",
        GitHubRepositoryBranch = "main"
    }, _baseAddress);

    // A base address other than api.github.com is treated like a GitHub Enterprise host: the API lives under /api/v3.
    private void Route(string path, int status, string body) => _routes["/api/v3" + path] = (status, body);

    [Fact]
    public async Task A_missing_repository_is_RepositoryNotFound()
    {
        Assert.Equal(GitHubRepositoryState.RepositoryNotFound, await Wrapper().GetRepositoryStateAsync());
    }

    [Fact]
    public async Task A_repository_with_its_branch_is_Ready()
    {
        Route("/repos/o/r", 200, """{"id":1,"name":"r"}""");
        Route("/repos/o/r/branches/main", 200, """{"name":"main"}""");

        Assert.Equal(GitHubRepositoryState.Ready, await Wrapper().GetRepositoryStateAsync());
    }

    [Fact]
    public async Task A_repository_without_the_branch_is_BranchNotFound_when_it_has_other_branches()
    {
        Route("/repos/o/r", 200, """{"id":1,"name":"r"}""");
        Route("/repos/o/r/branches", 200, """[{"name":"dev"}]""");

        Assert.Equal(GitHubRepositoryState.BranchNotFound, await Wrapper().GetRepositoryStateAsync());
    }

    [Fact]
    public async Task A_repository_without_any_branch_is_Empty()
    {
        Route("/repos/o/r", 200, """{"id":1,"name":"r"}""");
        Route("/repos/o/r/branches", 200, "[]");

        Assert.Equal(GitHubRepositoryState.Empty, await Wrapper().GetRepositoryStateAsync());
    }

    [Fact]
    public async Task A_rejected_token_is_an_error_not_a_state()
    {
        Route("/repos/o/r", 401, """{"message":"Bad credentials"}""");

        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => Wrapper().GetRepositoryStateAsync());

        Assert.Contains("HTTP 401", exception.Message);
    }

    [Fact]
    public async Task A_404_for_a_file_is_null_and_a_rejected_token_is_an_error()
    {
        var wrapper = Wrapper();
        Assert.Null(await wrapper.GetFileAsync("ck-models/v2/catalog.json"));

        Route("/repos/o/r/contents/ck-models/v3/catalog.json", 401, """{"message":"Bad credentials"}""");
        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => wrapper.GetFileAsync("ck-models/v3/catalog.json"));
        Assert.Contains("HTTP 401", exception.Message);
    }
}
