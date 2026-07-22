using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Xunit;

namespace ConstructionKit.Engine.SystemTests.ModelCatalogs;

/// <summary>
/// Live write tests against the real GitHub contents API (AB#4506 regression).
/// Opt-in: set CK_GITHUB_WRITE_TEST_REPO ("owner/repo" — use a SCRATCH
/// repository, the test commits and deletes files) and
/// CK_GITHUB_WRITE_TEST_TOKEN (a PAT with contents read/write on that repo).
/// Skipped when either variable is missing, so CI and normal local runs are
/// unaffected.
/// </summary>
public class GitHubClientWrapperWriteSystemTests
{
    [Fact]
    public async Task CreateFileAsync_OnExistingPath_FallsBackToUpdate()
    {
        var repo = Environment.GetEnvironmentVariable("CK_GITHUB_WRITE_TEST_REPO");
        var token = Environment.GetEnvironmentVariable("CK_GITHUB_WRITE_TEST_TOKEN");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(token),
            "CK_GITHUB_WRITE_TEST_REPO / CK_GITHUB_WRITE_TEST_TOKEN not set — live GitHub write test skipped.");

        var parts = repo!.Split('/');
        Assert.Equal(2, parts.Length);

        var options = new PublicGitHubCatalogOptions
        {
            GitHubRepositoryOwner = parts[0],
            GitHubRepositoryName = parts[1],
            GitHubRepositoryBranch = "main",
            GitHubApiToken = token
        };
        var wrapper = new GitHubClientWrapper(options);

        var path = $"write-tests/{Guid.NewGuid():N}/file.json";
        try
        {
            await wrapper.CreateFileAsync(path, "AB#4506 write test: create", "{\"v\":1}");

            // The AB#4506 regression: a second CREATE on an existing path — the
            // engine takes this path whenever its existence read was stale (Pages
            // lag / API read-after-write lag / parallel build). GitHub rejects the
            // PUT with 422 «"sha" wasn't supplied»; the wrapper's sha-conflict
            // fallback must recover by re-reading and updating instead of failing.
            await wrapper.CreateFileAsync(path, "AB#4506 write test: create on existing", "{\"v\":2}");

            var file = await wrapper.GetFileAsync(path);
            Assert.True(file.HasValue);
            Assert.Contains("\"v\":2", file!.Value.Item1);
        }
        finally
        {
            var leftover = await wrapper.GetFileAsync(path);
            if (leftover.HasValue)
            {
                await wrapper.DeleteFileAsync(path, "AB#4506 write test: cleanup", leftover.Value.Item2);
            }
        }
    }
}
