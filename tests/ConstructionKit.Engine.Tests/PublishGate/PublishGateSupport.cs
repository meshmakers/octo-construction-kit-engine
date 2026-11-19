using System.Net;
using System.Security.Cryptography;
using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6386: a writable, non-local catalog held in memory that behaves like the cache-backed GitHub catalogs where it
///     matters for the gate: model names are matched case-sensitively (<c>CachedCatalog</c>) unless switched off, and
///     every range lookup reports <see cref="SourceUnreachable" />. Counts the publishes and lets a test change the
///     catalog while a refresh runs (the race of the publish gate).
/// </summary>
internal sealed class StubCatalog : ICatalog, IReplaceablePublishCatalog
{
    public const string Name = "StubPublishedCatalog";

    private readonly Dictionary<CkModelId, CkCompiledModelRoot> _models = new();

    public bool SourceUnreachable { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public bool CanReadValue { get; set; } = true;
    public bool AllowsReplacingPublishedVersions { get; set; }
    public int PublishCount { get; private set; }
    public int RefreshCount { get; private set; }

    /// <summary>Called with the 1-based number of every refresh, before it returns.</summary>
    public Action<int>? OnRefresh { get; set; }

    public IReadOnlyCollection<CkModelId> ModelIds => _models.Keys;

    public int Order => 50;
    public string CatalogName => Name;
    public string Description => "In-memory published catalog (tests)";
    public bool CanWrite => true;
    public bool CanRead => CanReadValue;

    public void Seed(CkCompiledModelRoot model) => _models[model.ModelId] = model;

    public Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        RefreshCount++;
        OnRefresh?.Invoke(RefreshCount);
        return Task.CompletedTask;
    }

    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null) => sourceIdentifier == null;

    private bool NameMatches(string a, string b) =>
        string.Equals(a, b, CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        var highest = _models.Keys
            .Where(id => NameMatches(id.Name, modelIdVersionRange.Name) && modelIdVersionRange.IsSatisfiedBy(id))
            .OrderByDescending(id => id.Version)
            .FirstOrDefault();
        return Task.FromResult(new ModelExistingResult
        {
            Exists = highest != null, ModelId = highest, CatalogName = Name, SourceUnreachable = SourceUnreachable,
            CacheUpdatedAt = DateTime.UtcNow
        });
    }

    public Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null) =>
        Task.FromResult(_models.Keys.Any(id => NameMatches(id.Name, modelId.Name) && id.Version == modelId.Version));

    public Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var key = _models.Keys.FirstOrDefault(id => NameMatches(id.Name, modelId.Name) && id.Version == modelId.Version);
        return key != null
            ? Task.FromResult(_models[key])
            : throw ModelCatalogException.ModelNotFound(modelId, Name);
    }

    public Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        PublishCount++;
        _models[ckCompiledModel.ModelId] = ckCompiledModel;
        return Task.CompletedTask;
    }

    public Task<CatalogUnpublishResult> UnpublishAsync(CkModelId modelId, string? reason = null,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
        Task.FromResult(_models.Remove(modelId)
            ? CatalogUnpublishResult.Removed
            : throw ModelCatalogException.ModelNotFound(modelId, Name));

    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        await Task.CompletedTask;
        foreach (var model in _models.Values.ToList())
        {
            yield return new CatalogResultItem
            {
                CatalogName = Name, ModelId = model.ModelId, Description = model.Description
            };
        }
    }

    public IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier) =>
        ListAsync(sourceIdentifier);
}

/// <summary>
///     AB#6386: a GitHub repository held in memory. The same dictionary answers the authenticated Contents API
///     (<see cref="IGitHubClientWrapper" />) and the gh-pages mirror (<see cref="IHttpClientWrapper" />), so the real
///     <see cref="GitHubCatalog" /> publishes and reads its index files, both catalog roots and its case-sensitive cache
///     exactly as against GitHub.
/// </summary>
internal sealed class InMemoryGitHubRepository : IGitHubClientWrapper, IGitHubTreeReader, IHttpClientWrapper, IGitHubClientFactory,
    IHttpClientFactory
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>What the Contents API says about the repository itself (AB#6580).</summary>
    public GitHubRepositoryState State { get; set; } = GitHubRepositoryState.Ready;

    /// <summary>
    ///     Makes a Contents API call fail: called with the file path (<see cref="RepositoryStatePath" /> for the repository
    ///     query); a non-null result is thrown. The real wrapper throws exactly these after its retries.
    /// </summary>
    public Func<string, Exception?>? ApiFault { get; set; }

    public const string RepositoryStatePath = "<repository>";

    /// <summary>Every Contents API request this process-side client made (AB#6719), by kind; the mirror is not counted.</summary>
    public Dictionary<string, int> ApiCalls { get; } = new(StringComparer.Ordinal);

    /// <summary>The number of API requests so far (reads, listings and writes).</summary>
    public int ApiCallCount => ApiCalls.Values.Sum();

    /// <summary>Forgets the counted requests (call after seeding).</summary>
    public void ResetApiCalls()
    {
        ApiCalls.Clear();
        ApiTrace.Clear();
    }

    /// <summary>Counts one API request of <paramref name="kind" />.</summary>
    public void CountApiCall(string kind, string? path = null)
    {
        ApiCalls[kind] = ApiCalls.GetValueOrDefault(kind) + 1;
        ApiTrace.Add(path == null ? kind : $"{kind} {path}");
    }

    /// <summary>The counted requests in order, with the path where there is one.</summary>
    public List<string> ApiTrace { get; } = new();

    /// <summary>Makes a write or delete fail (AB#6582): called with the file path, a non-null result is thrown.</summary>
    public Func<string, Exception?>? WriteFault { get; set; }

    private Task? FailWrite(string path)
    {
        var fault = WriteFault?.Invoke(path);
        return fault == null ? null : Task.FromException(fault);
    }

    /// <summary>
    ///     Runs before every write or delete reaches the repository (AB#6584): the place where a test lets another
    ///     publisher - a second process - win the race between the baseline read and the write. Called with the file
    ///     path; the hook is cleared while it runs, so the other publisher's own writes do not trigger it again.
    /// </summary>
    public Func<string, Task>? BeforeWrite { get; set; }

    private async Task RunBeforeWriteAsync(string path)
    {
        var hook = BeforeWrite;
        if (hook == null)
        {
            return;
        }

        BeforeWrite = null;
        try
        {
            await hook(path).ConfigureAwait(false);
        }
        finally
        {
            // A hook that wants to fire again re-arms itself.
            BeforeWrite ??= hook;
        }
    }

    /// <summary>What the gh-pages mirror answers for a path (AB#6580): by default the repository content.</summary>
    public Func<string, PagesAnswer>? PagesBehavior { get; set; }

    /// <summary>The mirror as of an earlier deployment (AB#6581): when set, it serves this instead of <see cref="Files" />.</summary>
    public Dictionary<string, string>? PagesSnapshot { get; set; }

    /// <summary>The mirror stops following the repository from now on.</summary>
    public void FreezePages() => PagesSnapshot = new Dictionary<string, string>(Files, StringComparer.Ordinal);

    /// <summary>The mirror catches up.</summary>
    public void ThawPages() => PagesSnapshot = null;

    private Dictionary<string, string> PagesFiles => PagesSnapshot ?? Files;

    public IGitHubClientWrapper CreateClient(IGitHubOptions gitHubOptions) => this;
    public IHttpClientWrapper CreateClient(Uri baseAddress) => this;

    private static string Sha(string content) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content)));

    public Task<(string, string)?> GetFileAsync(string filePath)
    {
        CountApiCall("GetFile", filePath);
        var fault = ApiFault?.Invoke(filePath);
        if (fault != null)
        {
            return Task.FromException<(string, string)?>(fault);
        }

        return Task.FromResult<(string, string)?>(Files.TryGetValue(filePath, out var content) && State is
            GitHubRepositoryState.Ready
            ? (content, Sha(content))
            : null);
    }

    /// <summary>Set to false for a client without <see cref="IGitHubTreeReader" /> semantics: GitHub truncated the tree.</summary>
    public bool TreeTruncated { get; set; }

    public Task<(IReadOnlyList<string> Paths, bool Truncated)> ListBranchFilePathsAsync()
    {
        CountApiCall("Tree");
        var fault = ApiFault?.Invoke("<tree>");
        return fault != null
            ? Task.FromException<(IReadOnlyList<string>, bool)>(fault)
            : Task.FromResult<(IReadOnlyList<string>, bool)>((Files.Keys.ToList(), TreeTruncated));
    }

    public Task<GitHubRepositoryState> GetRepositoryStateAsync()
    {
        CountApiCall("RepositoryState");
        var fault = ApiFault?.Invoke(RepositoryStatePath);
        return fault != null ? Task.FromException<GitHubRepositoryState>(fault) : Task.FromResult(State);
    }

    public async Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha)
    {
        CountApiCall("UpdateFile");
        await RunBeforeWriteAsync(filePath);
        if (FailWrite(filePath) is { } failedUpdate)
        {
            await failedUpdate;
        }

        if (!Files.TryGetValue(filePath, out var existing) || Sha(existing) != sha)
        {
            throw new InvalidOperationException($"SHA conflict on '{filePath}'");
        }

        Files[filePath] = content;
    }

    public async Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        CountApiCall("CreateFile");
        await RunBeforeWriteAsync(filePath);
        if (FailWrite(filePath) is { } failedCreate)
        {
            await failedCreate;
        }

        if (!Files.TryAdd(filePath, content))
        {
            throw new InvalidOperationException($"'{filePath}' already exists");
        }
    }

    public async Task UpsertFileWithMergeAsync(string filePath, string commitMessage, Func<string?, string?> merge)
    {
        CountApiCall("UpsertFile");
        await RunBeforeWriteAsync(filePath);
        if (FailWrite(filePath) is { } failedUpsert)
        {
            await failedUpsert;
        }

        Files.TryGetValue(filePath, out var existing);
        var merged = merge(existing);
        if (merged != null)
        {
            Files[filePath] = merged;
        }
    }

    public async Task DeleteFileAsync(string filePath, string commitMessage, string sha)
    {
        CountApiCall("DeleteFile");
        await RunBeforeWriteAsync(filePath);
        if (FailWrite(filePath) is { } failedDelete)
        {
            await failedDelete;
        }

        Files.Remove(filePath);
    }

    public Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath)
    {
        CountApiCall("ListFiles");
        return Task.FromResult<IReadOnlyList<(string path, string sha)>>(Files
            .Where(f => f.Key.StartsWith(directoryPath.TrimEnd('/') + "/", StringComparison.Ordinal))
            .Select(f => (f.Key, Sha(f.Value))).ToList());
    }

    private PagesAnswer PagesAnswerFor(string? requestUri)
    {
        var answer = PagesBehavior?.Invoke(requestUri ?? "") ?? PagesAnswer.Serve;
        return answer == PagesAnswer.Serve && (requestUri == null || !PagesFiles.ContainsKey(requestUri))
            ? PagesAnswer.NotFound
            : answer;
    }

    public Task<string?> GetStringAsync(string? requestUri) =>
        PagesAnswerFor(requestUri) switch
        {
            PagesAnswer.Serve => Task.FromResult<string?>(PagesFiles[requestUri!]),
            PagesAnswer.NotFound => throw new HttpRequestException($"404 {requestUri}", null, HttpStatusCode.NotFound),
            PagesAnswer.ServerError => throw new HttpRequestException($"500 {requestUri}", null,
                HttpStatusCode.InternalServerError),
            PagesAnswer.Timeout => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            _ => throw new HttpRequestException("Connection refused")
        };

    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken) =>
        PagesAnswerFor(requestUri) switch
        {
            PagesAnswer.Serve => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(PagesFiles[requestUri!]) }),
            PagesAnswer.NotFound => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            PagesAnswer.ServerError => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            PagesAnswer.Timeout => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            _ => throw new HttpRequestException("Connection refused")
        };

    /// <summary>A byte-level fingerprint of the whole repository: path and content hash of every file.</summary>
    public string Snapshot() =>
        string.Join("|", Files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{Sha(f.Value)}"));
}

/// <summary>How the gh-pages mirror of an <see cref="InMemoryGitHubRepository" /> answers one request.</summary>
public enum PagesAnswer
{
    /// <summary>The file, when the mirror has it (otherwise 404).</summary>
    Serve,
    NotFound,
    ServerError,
    Timeout,
    Offline
}

/// <summary>
///     The real <see cref="GitHubCatalog" /> over an <see cref="InMemoryGitHubRepository" />, as the private catalog or,
///     with <paramref name="isLane" />, as the development lane catalog.
/// </summary>
internal sealed class RepositoryBackedCatalog : GitHubCatalog
{
    public const string Name = "RepositoryBackedCatalog";

    public RepositoryBackedCatalog(ICkJsonSerializer serializer, InMemoryGitHubRepository repository,
        string cacheDirectory, bool isLane = false, CatalogApiReadMode apiReads = CatalogApiReadMode.Enabled,
        string? token = "t")
        : base(serializer, repository, repository, Options(cacheDirectory, isLane, apiReads, token), 15, Name,
            "In-memory GitHub catalog")
    {
    }

    private static PrivateGitHubCatalogOptions Options(string cacheDirectory, bool isLane, CatalogApiReadMode apiReads,
        string? token) => new()
    {
        CacheDirectory = cacheDirectory,
        GitHubPagesUri = "https://test.github.io/repo",
        GitHubRepositoryOwner = "meshmakers",
        GitHubRepositoryName = isLane ? LaneCatalogRepositoryName : "construction-kit-libraries-build",
        GitHubRepositoryBranch = "main",
        GitHubApiToken = token,
        ApiReads = apiReads,
        RefreshNotFoundRetryCount = 0,
        RefreshNotFoundRetryDelaySeconds = 0
    };
}

/// <summary>Compiled models for the gate tests: no sources, no compile step.</summary>
internal static class GateModels
{
    public const string Name = "GateModel";

    /// <summary>
    ///     A self-contained model with one enum <c>State</c> whose values are the part the tests change, and one
    ///     attribute. <paramref name="ckLanguage" /> 2 is a hard-gated, v3-root model; null is a classic v1 model.
    /// </summary>
    public static CkCompiledModelRoot Create(string version, string enumValues = "A,B", int? ckLanguage = 2,
        string? name = null, bool withSerial = true) =>
        new()
        {
            ModelId = new CkModelId(name ?? Name, version),
            Description = "Gate fixture",
            CkLanguage = ckLanguage,
            Attributes = withSerial
                ? [new CkAttributeDto { AttributeId = "Serial", ValueType = AttributeValueTypesDto.String }]
                : [new CkAttributeDto { AttributeId = "Other", ValueType = AttributeValueTypesDto.String }],
            Enums =
            [
                new CkEnumDto
                {
                    EnumId = "State",
                    Values = enumValues.Split(',').Select((v, i) => new CkEnumValueDto { Key = i, Name = v }).ToList()
                }
            ]
        };
}
