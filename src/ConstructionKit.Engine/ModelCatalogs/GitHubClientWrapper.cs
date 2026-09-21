using System.Net;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

internal class GitHubClientWrapper : IGitHubClientWrapper
{
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 500;

    // Octokit's underlying HttpClient defaults to 100 s per request. That is too long for build
    // pipelines: when GitHub's API stalls or rate-limits, a single stuck call freezes CkCompile /
    // ckc publish for the full timeout and bubbles up as a cryptic "A task was canceled". 60 s is
    // generous for healthy paginated reads but fails fast on real hangs.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private readonly IGitHubOptions gitHubOptions;
    private readonly GitHubClient _client;

    public GitHubClientWrapper(IGitHubOptions gitHubOptions)
    {
        this.gitHubOptions = gitHubOptions;
        _client = new GitHubClient(new ProductHeaderValue(gitHubOptions.ProductName))
        {
            Credentials = new Credentials(gitHubOptions.GitHubApiToken)
        };
        _client.SetRequestTimeout(RequestTimeout);
    }

    public async Task<(string, string)?> GetFileAsync(string filePath)
    {
        // The read is half of every write: the publish pre-reads each file to pick create vs
        // update, and the SHA-conflict recoveries re-read from inside a catch body, where a
        // sibling catch cannot help. So the retry policy has to live HERE too (AB#5298 review) -
        // otherwise a rate-limited or 5xx read still aborts the publish with Octokit's generic
        // message, exactly the failure the write-side retry was added against.
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Currently there is no way to check if a file exists without trying to get it and catching the exception
                var file = await _client.Repository.Content.GetAllContentsByRef(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    gitHubOptions.GitHubRepositoryBranch).ConfigureAwait(false);
                if (file.Count == 0)
                {
                    return null;
                }
                return (file.First().Content, file.First().Sha);
            }
            catch (NotFoundException)
            {
                return null;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("read", filePath, ex, attempt + 1);
            }
        }

        throw new InvalidOperationException($"Unreachable: GetFileAsync('{filePath}') left its retry loop");
    }

    public async Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha)
    {
        var currentSha = sha;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.UpdateFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new UpdateFileRequest(commitMessage, content, currentSha)).ConfigureAwait(false);
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var refreshed = await GetFileAsync(filePath).ConfigureAwait(false);
                if (!refreshed.HasValue)
                {
                    throw;
                }
                currentSha = refreshed.Value.Item2;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("update", filePath, ex, attempt + 1);
            }
        }
    }

    public async Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.CreateFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new CreateFileRequest(commitMessage, content)).ConfigureAwait(false);
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                // File may have been created by a parallel build — try updating instead
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var existing = await GetFileAsync(filePath).ConfigureAwait(false);
                if (existing.HasValue)
                {
                    await UpdateFileAsync(filePath, commitMessage, content, existing.Value.Item2)
                        .ConfigureAwait(false);
                    return;
                }
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("create", filePath, ex, attempt + 1);
            }
        }
    }

    public async Task UpsertFileWithMergeAsync(string filePath, string commitMessage, Func<string?, string?> merge)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var existing = await GetFileAsync(filePath).ConfigureAwait(false);
            var content = merge(existing?.Item1);
            if (content == null)
            {
                return;
            }

            try
            {
                // Raw Octokit calls, NOT UpdateFileAsync/CreateFileAsync: their internal SHA-conflict
                // retry rewrites the SAME content with a fresh SHA, which would silently clobber the
                // concurrent writer's changes. Here the conflict has to bubble up so the next loop
                // iteration re-reads and re-merges on top of the winner's content.
                if (existing.HasValue)
                {
                    await _client.Repository.Content.UpdateFile(
                        gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                        new UpdateFileRequest(commitMessage, content, existing.Value.Item2)).ConfigureAwait(false);
                }
                else
                {
                    await _client.Repository.Content.CreateFile(
                        gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                        new CreateFileRequest(commitMessage, content)).ConfigureAwait(false);
                }

                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("upsert", filePath, ex, attempt + 1);
            }
        }
    }

    public async Task DeleteFileAsync(string filePath, string commitMessage, string sha)
    {
        var currentSha = sha;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _client.Repository.Content.DeleteFile(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, filePath,
                    new DeleteFileRequest(commitMessage, currentSha, gitHubOptions.GitHubRepositoryBranch))
                    .ConfigureAwait(false);
                return;
            }
            catch (NotFoundException)
            {
                // Already gone — deletion is idempotent.
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsShaConflict(ex))
            {
                await Task.Delay(BaseDelayMs * (attempt + 1)).ConfigureAwait(false);
                var refreshed = await GetFileAsync(filePath).ConfigureAwait(false);
                if (!refreshed.HasValue)
                {
                    // File disappeared between attempts — nothing left to delete.
                    return;
                }
                currentSha = refreshed.Value.Item2;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("delete", filePath, ex, attempt + 1);
            }
        }
    }

    public async Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath)
    {
        // Directory-scoped enumeration: walk the Contents API from the target directory down, instead of
        // pulling the whole repository tree and filtering. This scales with the blueprint subtree rather
        // than the repository, avoids the Git-Trees truncation cliff for large repos, and returns the blob
        // SHA each file's DeleteFile call requires. A missing directory yields an empty list.
        var files = new List<(string path, string sha)>();
        await CollectFilesRecursiveAsync(directoryPath.TrimEnd('/'), files).ConfigureAwait(false);
        return files;
    }

    private async Task CollectFilesRecursiveAsync(string directoryPath, List<(string path, string sha)> files)
    {
        IReadOnlyList<RepositoryContent>? contents = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                contents = await _client.Repository.Content.GetAllContentsByRef(
                    gitHubOptions.GitHubRepositoryOwner, gitHubOptions.GitHubRepositoryName, directoryPath,
                    gitHubOptions.GitHubRepositoryBranch).ConfigureAwait(false);
                break;
            }
            catch (NotFoundException)
            {
                return;
            }
            catch (ApiException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                await Task.Delay(RetryDelay(ex, attempt)).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                throw Enrich("list", directoryPath, ex, attempt + 1);
            }
        }

        if (contents == null)
        {
            throw new InvalidOperationException($"Unreachable: listing '{directoryPath}' left its retry loop");
        }

        foreach (var item in contents)
        {
            switch (item.Type.Value)
            {
                case ContentType.File:
                    files.Add((item.Path, item.Sha));
                    break;
                case ContentType.Dir:
                    await CollectFilesRecursiveAsync(item.Path, files).ConfigureAwait(false);
                    break;
                // Symlinks / submodules are not part of a blueprint payload; ignore them.
            }
        }
    }

    /// <summary>
    /// AB#5298: the second class of "try again", next to the SHA conflicts below. A release
    /// publishes the CK model and the blueprint into the SAME catalog repository within minutes,
    /// dozens of single-file commits each. GitHub answers bursts like that with a secondary rate
    /// limit (403, Octokit: <see cref="SecondaryRateLimitExceededException" /> /
    /// <see cref="AbuseException" />), and its Contents API also throws the occasional 5xx while
    /// the branch is being written to. None of those matched <see cref="IsShaConflict" />, so a
    /// single such answer killed the whole publish - and the log carried only Octokit's generic
    /// message (see <see cref="Enrich" />). Retrying these is safe: the request either did not
    /// reach the repository or is a plain retry of an idempotent content write.
    /// </summary>
    internal static bool IsTransient(ApiException ex)
    {
        if (ex is SecondaryRateLimitExceededException or AbuseException or RateLimitExceededException)
        {
            return true;
        }

        var status = (int)ex.StatusCode;
        if (status >= 500)
        {
            return true;
        }

        // Belt and braces for a 403 that Octokit did not type: GitHub's own text names it.
        return ex.StatusCode == HttpStatusCode.Forbidden
               && (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                   || (ex.ApiError?.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    /// <summary>
    /// Exponential back-off from <see cref="BaseDelayMs" />, capped at 30 s. A primary rate limit
    /// tells us when it resets; wait for that instead (capped at 60 s) - retrying earlier is
    /// guaranteed to fail again.
    /// </summary>
    internal static TimeSpan RetryDelay(ApiException ex, int attempt)
    {
        var backoff = TimeSpan.FromMilliseconds(BaseDelayMs * Math.Pow(2, attempt));
        if (backoff > TimeSpan.FromSeconds(30))
        {
            backoff = TimeSpan.FromSeconds(30);
        }

        if (ex is RateLimitExceededException limit)
        {
            var untilReset = limit.Reset - DateTimeOffset.UtcNow;
            if (untilReset > backoff)
            {
                backoff = untilReset > TimeSpan.FromSeconds(60) ? TimeSpan.FromSeconds(60) : untilReset;
            }
        }

        return backoff;
    }

    /// <summary>
    /// Wrap an exhausted or non-retriable <see cref="ApiException" /> with what Octokit's message
    /// leaves out: the status code, GitHub's error text, the file and the attempt count. Before
    /// AB#5298 the publish log for a failed release read "An error occurred with this API
    /// request" and nothing else.
    /// </summary>
    private static Exception Enrich(string operation, string path, ApiException ex, int attempts)
    {
        return ModelCatalogException.GitHubRequestFailed(operation, path, (int)ex.StatusCode,
            ex.ApiError?.Message, attempts, ex);
    }

    private static bool IsShaConflict(ApiException ex)
    {
        // Three shapes of "the file is not in the state your request assumed":
        //  - 409 Conflict
        //  - 422 "... but expected ..." — update carried a stale sha
        //  - 422 «"sha" wasn't supplied.» — CREATE hit a file that already exists
        //    (GitHub treats the create as an update and demands the sha). Seen in
        //    AB#4506: the second CkCompile publish pass within one build re-created
        //    catalog files committed seconds earlier by the first pass, because the
        //    existence read still returned stale data. The CreateFileAsync fallback
        //    below (re-read → update) recovers exactly this case.
        return ex.Message.Contains("but expected", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("\"sha\" wasn't supplied", StringComparison.OrdinalIgnoreCase)
               || ex.StatusCode == System.Net.HttpStatusCode.Conflict;
    }
}
