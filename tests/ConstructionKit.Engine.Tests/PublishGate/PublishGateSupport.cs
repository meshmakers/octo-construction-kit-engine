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
internal sealed class InMemoryGitHubRepository : IGitHubClientWrapper, IHttpClientWrapper, IGitHubClientFactory,
    IHttpClientFactory
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public IGitHubClientWrapper CreateClient(IGitHubOptions gitHubOptions) => this;
    public IHttpClientWrapper CreateClient(Uri baseAddress) => this;

    private static string Sha(string content) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(content)));

    public Task<(string, string)?> GetFileAsync(string filePath) =>
        Task.FromResult<(string, string)?>(Files.TryGetValue(filePath, out var content) ? (content, Sha(content)) : null);

    public Task UpdateFileAsync(string filePath, string commitMessage, string content, string sha)
    {
        if (!Files.TryGetValue(filePath, out var existing) || Sha(existing) != sha)
        {
            throw new InvalidOperationException($"SHA conflict on '{filePath}'");
        }

        Files[filePath] = content;
        return Task.CompletedTask;
    }

    public Task CreateFileAsync(string filePath, string commitMessage, string content)
    {
        if (!Files.TryAdd(filePath, content))
        {
            throw new InvalidOperationException($"'{filePath}' already exists");
        }

        return Task.CompletedTask;
    }

    public Task UpsertFileWithMergeAsync(string filePath, string commitMessage, Func<string?, string?> merge)
    {
        Files.TryGetValue(filePath, out var existing);
        var merged = merge(existing);
        if (merged != null)
        {
            Files[filePath] = merged;
        }

        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string filePath, string commitMessage, string sha)
    {
        Files.Remove(filePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string path, string sha)>> ListFilesRecursiveAsync(string directoryPath) =>
        Task.FromResult<IReadOnlyList<(string path, string sha)>>(Files
            .Where(f => f.Key.StartsWith(directoryPath.TrimEnd('/') + "/", StringComparison.Ordinal))
            .Select(f => (f.Key, Sha(f.Value))).ToList());

    public Task<string?> GetStringAsync(string? requestUri) =>
        requestUri != null && Files.TryGetValue(requestUri, out var content)
            ? Task.FromResult<string?>(content)
            : throw new HttpRequestException($"404 {requestUri}", null, HttpStatusCode.NotFound);

    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken) =>
        Task.FromResult(requestUri != null && Files.TryGetValue(requestUri, out var content)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));

    /// <summary>A byte-level fingerprint of the whole repository: path and content hash of every file.</summary>
    public string Snapshot() =>
        string.Join("|", Files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{Sha(f.Value)}"));
}

/// <summary>
///     The real <see cref="GitHubCatalog" /> over an <see cref="InMemoryGitHubRepository" />, as the private catalog or,
///     with <paramref name="isLane" />, as the development lane catalog.
/// </summary>
internal sealed class RepositoryBackedCatalog : GitHubCatalog
{
    public const string Name = "RepositoryBackedCatalog";

    public RepositoryBackedCatalog(ICkJsonSerializer serializer, InMemoryGitHubRepository repository,
        string cacheDirectory, bool isLane = false)
        : base(serializer, repository, repository, Options(cacheDirectory, isLane), 15, Name, "In-memory GitHub catalog")
    {
    }

    private static PrivateGitHubCatalogOptions Options(string cacheDirectory, bool isLane) => new()
    {
        CacheDirectory = cacheDirectory,
        GitHubPagesUri = "https://test.github.io/repo",
        GitHubRepositoryOwner = "meshmakers",
        GitHubRepositoryName = isLane ? LaneCatalogRepositoryName : "construction-kit-libraries-build",
        GitHubRepositoryBranch = "main",
        GitHubApiToken = "t",
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
