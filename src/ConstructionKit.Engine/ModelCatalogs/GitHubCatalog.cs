using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Public catalog on GitHub for construction kit models
/// </summary>
public class PublicGitHubCatalog(
    ICkJsonSerializer ckJsonSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PublicGitHubCatalogOptions> gitHubOptions) : GitHubCatalog(ckJsonSerializer, httpClientFactory,
    gitHubClientFactory, gitHubOptions.Value, 20, "PublicGitHubCatalog",
    GitHubCatalogDescription.ForRepository("Public GitHub catalog", gitHubOptions.Value));

/// <summary>
/// Private catalog on GitHub for construction kit models.
/// Checked before PublicGitHubCatalog so dev models take precedence.
/// </summary>
public class PrivateGitHubCatalog(
    ICkJsonSerializer ckJsonSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PrivateGitHubCatalogOptions> gitHubOptions) : GitHubCatalog(ckJsonSerializer, httpClientFactory,
    gitHubClientFactory, gitHubOptions.Value, 15, "PrivateGitHubCatalog",
    GitHubCatalogDescription.ForRepository("Private GitHub catalog for development and testing",
        gitHubOptions.Value));

/// <summary>
/// Appends the repository coordinates to a catalog's human-readable description.
/// </summary>
/// <remarks>
/// AB#5139 makes the private catalog slots lane-scoped: the same catalog name
/// (<c>PrivateGitHubCatalog</c> / <c>PrivateGitHubBlueprintCatalog</c>) is bound to a different
/// GitHub repository per installation — main test-2 keeps <c>construction-kit-libraries-build</c> /
/// <c>blueprint-libraries-build</c>, the 0.2-dev instance points at <c>meshmakers/octo-catalog-dev</c>.
/// The catalog name is the identity everything else keys on and must not change, so without the
/// coordinates in the description two installations are indistinguishable in <c>octo-cli
/// ListCatalogs</c>, in the GraphQL <c>catalogs</c> field and in Refinery Studio — and "why is my
/// model not in the private catalog?" cannot be answered from the UI at all. The prose prefix is
/// kept verbatim so anything grepping logs or docs for it still matches.
/// </remarks>
internal static class GitHubCatalogDescription
{
    /// <summary>
    /// Renders <c>"{description} (owner/repo@branch)"</c>, degrading gracefully when the options
    /// carry no repository: an unset owner or branch is simply left out rather than producing a
    /// dangling separator, and without a repository name the plain description is returned so no
    /// empty parentheses ever reach the UI.
    /// </summary>
    public static string ForRepository(string description, IGitHubOptions gitHubOptions)
    {
        var repositoryName = gitHubOptions.GitHubRepositoryName;
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return description;
        }

        var owner = gitHubOptions.GitHubRepositoryOwner;
        var branch = gitHubOptions.GitHubRepositoryBranch;

        var repository = string.IsNullOrWhiteSpace(owner) ? repositoryName : owner + "/" + repositoryName;
        if (!string.IsNullOrWhiteSpace(branch))
        {
            repository += "@" + branch;
        }

        return description + " (" + repository + ")";
    }
}

/// <summary>
/// Construction kit model catalog for GitHub base class
/// </summary>
public abstract class GitHubCatalog : CachedCatalog
{
    private const string RootPath = "ck-models/v2/";
    private const string CatalogFileName = "catalog.json";
    private const int MaxCacheFileAgeSeconds = 60;

    private static readonly System.Text.Json.JsonSerializerOptions CatalogJsonReadOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static readonly System.Text.Json.JsonSerializerOptions CatalogJsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly GitHubCatalogOptions _gitHubOptions;

    // Static gh-pages reads cost nothing against the per-user 5000/h GitHub REST quota
    // that Octokit pulls from. Prefer the pages URL for every read; the Octokit client is
    // only fallback for setups that explicitly disabled gh-pages, plus the publish path.
    private bool IsPagesUriConfigured => !string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri);

    /// <summary>
    /// Creates a new instance of the <see cref="Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs.GitHubCatalog"/> class.
    /// </summary>
    protected GitHubCatalog(ICkJsonSerializer ckJsonSerializer, IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory,
        GitHubCatalogOptions gitHubOptions, int order, string catalogName, string description) : base(order,
        catalogName, description, gitHubOptions.IsEnabled, gitHubOptions.IsEnabled, gitHubOptions)
    {
        _ckJsonSerializer = ckJsonSerializer;
        _httpClientFactory = httpClientFactory;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubOptions = gitHubOptions;
    }

    /// <inheritdoc />
    public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public override async Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var pagesUrl = CreatePath(modelId);

        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                var response = await httpClient.GetAsync(pagesUrl, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
#if NETSTANDARD2_0
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                    var ckCompiledModelRoot = await _ckJsonSerializer
                        .DeserializeCompiledModelRootAsync(stream, "", operationResult,
                            tolerantToUnknownProperties: true).ConfigureAwait(false);
                    if (operationResult.HasErrors)
                    {
                        throw ModelCatalogException.ErrorDuringModelLoad(modelId, CatalogName,
                            operationResult);
                    }

                    return ckCompiledModelRoot;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
                }

                throw ModelCatalogException.InvalidGitHubRepository(CatalogName,
                    _gitHubOptions.GitHubPagesUri);
            }
            catch (HttpRequestException)
            {
                throw ModelCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
            }
            catch (TaskCanceledException)
            {
                throw ModelCatalogException.RequestTimeoutGitHubRepository(CatalogName,
                    _gitHubOptions.GitHubPagesUri);
            }
        }

        var gitHubClient = CreateGitHubClient();

        var r = await gitHubClient.GetFileAsync(pagesUrl).ConfigureAwait(false);
        if (r == null)
        {
            throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
        }

        var ckCompiledModelRoot2 = await _ckJsonSerializer
            .DeserializeCompiledModelRootAsync(r.Value.Item1, "", operationResult,
                tolerantToUnknownProperties: true).ConfigureAwait(false);
        if (operationResult.HasErrors)
        {
            throw ModelCatalogException.ErrorDuringModelLoad(modelId, CatalogName,
                operationResult);
        }
        return ckCompiledModelRoot2;
    }

    /// <inheritdoc />
    public override async Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();
        string filePath = CreatePath(ckCompiledModel.ModelId);

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            var content = await ReadContentAsync(ckCompiledModel).ConfigureAwait(false);

            var existing = await gitHubClient.GetFileAsync(filePath).ConfigureAwait(false);
            if (existing.HasValue)
            {
                if (!force)
                {
                    throw ModelCatalogException.ModelAlreadyExists(ckCompiledModel.ModelId, CatalogName);
                }

                await gitHubClient.UpdateFileAsync(filePath, $"Update to {ckCompiledModel.ModelId.FullName}", content,
                    existing.Value.Item2).ConfigureAwait(false);
            }
            else
            {
                await gitHubClient.CreateFileAsync(filePath, $"First commit for {ckCompiledModel.ModelId.FullName}",
                        content)
                    .ConfigureAwait(false);
            }

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the major version
            await UpdateModelVersionsCatalogAsync(ckCompiledModel.ModelId, ckCompiledModel.Description, gitHubClient)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the overall model library catalog
            await UpdateModelLibraryCatalogAsync(ckCompiledModel.ModelId, gitHubClient)
                .ConfigureAwait(false);

            // Update the root catalog
            await UpdateRootCatalogAsync(ckCompiledModel.ModelId, gitHubClient).ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();
        }
        catch (ApiException e)
        {
            throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, e);
        }

        try
        {
            // Refresh the local read cache. Best effort only: the refresh reads the gh-pages
            // mirror, which lags the commits just pushed and briefly serves 404 while a Pages
            // deployment is being replaced. A transient failure here must not fail a publish
            // that already succeeded (AB#4872: overlapping Pages deploys of two CI builds made
            // exactly this refresh kill both builds minutes after the models were safely
            // published). The cache has a 60 s max age, so the next read refreshes again.
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ModelCatalogException or HttpRequestException or TaskCanceledException)
        {
            // Swallow — the publish itself is complete.
        }
    }

    private async Task<string> ReadContentAsync(CkCompiledModelRoot ckCompiledModel)
    {
        using var memoryStream = new MemoryStream();
        var streamWriter = new StreamWriter(memoryStream);
        await _ckJsonSerializer.SerializeAsync(streamWriter, ckCompiledModel).ConfigureAwait(false);

        // Ensure all data is written to the MemoryStream
        await streamWriter.FlushAsync().ConfigureAwait(false);

        // Convert the MemoryStream to a string
        memoryStream.Position = 0;

        using var streamReader = new StreamReader(memoryStream);
        return await streamReader.ReadToEndAsync().ConfigureAwait(false);
    }

    private IGitHubClientWrapper CreateGitHubClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken) ||
            _gitHubOptions.GitHubApiToken == null)
        {
            throw ModelCatalogException.GitHubTokenMissing();
        }

        return _gitHubClientFactory.CreateClient(_gitHubOptions);
    }

    private string CreatePath(CkModelId ckModelId)
    {
        return RootPath
               + ckModelId.Name[0].ToString().ToLower() + "/"
               + ckModelId.Name + "/"
               + ckModelId.Version.Major
               + "/ck-" + ckModelId.Name.ToLower() + "-" + ckModelId.Version + ".json";
    }

    private IHttpClientWrapper CreateHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri) ||
            _gitHubOptions.GitHubPagesUri == null)
        {
            throw ModelCatalogException.GitHubPagesUriMissing();
        }

        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');

        return _httpClientFactory.CreateClient(new Uri($"{baseUri}"));
    }

    /// <summary>
    /// Gets the catalog for a specific major version of a model
    /// </summary>
    /// <param name="modelName">The model ID (without version)</param>
    /// <param name="majorVersion">The major version number</param>
    /// <returns>The major version catalog content or null if not found</returns>
    private async Task<SharedCatalogTypes.ModelLibraryVersionsCatalog?> GetModelLibraryVersionsCatalogAsync(
        string modelName, int majorVersion)
    {
        var catalogPath = $"{RootPath}{modelName[0].ToString().ToLower()}/{modelName}/{majorVersion}/{CatalogFileName}";

        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw ModelCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
            }
        }
        else
        {
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return null;
            }

            response = r.Value.Item1;
        }

        if (!string.IsNullOrWhiteSpace(response) && response != null)
        {
            var versionsCatalog = System.Text.Json.JsonSerializer
                .Deserialize<SharedCatalogTypes.ModelLibraryVersionsCatalog>(
                    response,
                    new System.Text.Json.JsonSerializerOptions
                        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            return versionsCatalog;
        }

        return null;
    }

    // The publish path reads AND writes the catalog index files through the authenticated GitHub
    // Contents API (inside UpsertFileWithMergeAsync) and never through the gh-pages URL: Pages
    // deploys lag behind the repository and briefly serve 404 while a deployment is replaced,
    // which both failed builds ("index files are missing", AB#4872 — originally fixed via
    // AB#2826/AB#2831) and corrupted merges (a stale Pages read dropping a concurrent build's
    // freshly published version from the catalog). Quota note before swinging this pendulum
    // again: these are ~3 extra API reads per published model on top of the ~8 mutation calls a
    // publish already makes. The 5000/h-per-user REST-quota incident that moved reads to
    // gh-pages (commit b364570, octo-construction-kit-CI 33317) was caused by the
    // O(models × majors) refresh walk, which deliberately stays on gh-pages.
    private static async Task UpdateModelVersionsCatalogAsync(CkModelId modelId, string? description,
        IGitHubClientWrapper gitHubClient)
    {
        // Catalog file path for this major version
        var catalogPath =
            $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update catalog for {modelId.Name} v{modelId.Version.Major}",
                existingJson => MergeModelVersionsCatalog(existingJson, modelId, description))
            .ConfigureAwait(false);
    }

    internal static string? MergeModelVersionsCatalog(string? existingJson, CkModelId modelId, string? description)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.ModelLibraryVersionsCatalog>(existingJson);
        var isModified = false;

        catalogData ??= new SharedCatalogTypes.ModelLibraryVersionsCatalog
        {
            ModelId = modelId.Name,
            MajorVersion = modelId.Version.Major,
            Versions = new List<SharedCatalogTypes.ModelLibraryVersionsCatalogEntry>()
        };
        if (catalogData.Description != description)
        {
            isModified = true;
            catalogData.Description = description;
        }

        // Create a dictionary to merge versions (preserving timestamps)
        var versionDict = catalogData.Versions.ToDictionary(k => k.Version, v => v);

        // Check if the current version already exists in the catalog
        var currentVersionString = modelId.Version.ToString();
        if (!versionDict.ContainsKey(currentVersionString))
        {
            // Add the new version only if it doesn't exist
            var fileName = $"ck-{modelId.Name.ToLower()}-{currentVersionString}.json";
            var filePath =
                $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{fileName}";

            versionDict[currentVersionString] = new SharedCatalogTypes.ModelLibraryVersionsCatalogEntry
            {
                Version = currentVersionString,
                FileName = fileName,
                PublishedAt = DateTime.UtcNow,
                FilePath = filePath
            };

            catalogData.Versions.Clear();
            catalogData.Versions.AddRange(versionDict.Values.OrderBy(v => v.Version));
            isModified = true;
        }

        if (!isModified)
        {
            return null;
        }

        // Sort versions in descending order (latest first)
        var sortedVersions = versionDict.Values
            .OrderByDescending(v => new CkVersion(v.Version))
            .ToList();

        catalogData.UpdatedAt = DateTime.UtcNow;
        catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

        return SerializeCatalog(catalogData);
    }

    private static T? DeserializeCatalog<T>(string? json) where T : class
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<T>(json!, CatalogJsonReadOptions);
    }

    private static string SerializeCatalog<T>(T catalog)
    {
        return System.Text.Json.JsonSerializer.Serialize(catalog, CatalogJsonWriteOptions);
    }


    /// <summary>
    /// Refreshes the catalog file by scanning all model directories
    /// </summary>
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        return RefreshCatalogAsync(forceRefresh);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh)
    {
        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdatedAsync(maxAge))
        {
            return;
        }

        var cache = await ReadCacheAsync(false).ConfigureAwait(false);

        // gh-pages briefly serves 404 for the whole site while a Pages deployment is being
        // replaced (AB#4872). An index file that should exist — the root index, or a sub-catalog
        // the root index lists — answering 404 is therefore usually transient: retry a few times
        // before treating it as missing. The budget is shared across the whole refresh so a full
        // site swap cannot stall a refresh for minutes.
        var notFoundRetryBudget = _gitHubOptions.RefreshNotFoundRetryCount;
        var notFoundRetryDelay = TimeSpan.FromSeconds(_gitHubOptions.RefreshNotFoundRetryDelaySeconds);

        async Task<T?> ReadWithNotFoundRetryAsync<T>(Func<Task<T?>> read) where T : class
        {
            var result = await read().ConfigureAwait(false);
            while (result == null && notFoundRetryBudget > 0)
            {
                notFoundRetryBudget--;
                if (notFoundRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(notFoundRetryDelay).ConfigureAwait(false);
                }

                result = await read().ConfigureAwait(false);
            }

            return result;
        }

        var (catalog, sourceUnreachable) = await GetRootCatalogWithReachabilityAsync().ConfigureAwait(false);
        while (catalog == null && !sourceUnreachable && notFoundRetryBudget > 0)
        {
            // A missing root index is either a genuinely empty catalog or the deploy swap —
            // retry before caching "empty", so models don't transiently vanish mid-deploy.
            notFoundRetryBudget--;
            if (notFoundRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(notFoundRetryDelay).ConfigureAwait(false);
            }

            (catalog, sourceUnreachable) = await GetRootCatalogWithReachabilityAsync().ConfigureAwait(false);
        }

        CacheTypes.CacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow,
            SourceUnreachable = sourceUnreachable
        };

        if (catalog != null)
        {
            foreach (var rootCatalogEntry in catalog.Models)
            {
                var modelLibraryCatalog = await ReadWithNotFoundRetryAsync(() =>
                    GetModelLibraryCatalogAsync(rootCatalogEntry.CatalogPath)).ConfigureAwait(false);

                if (modelLibraryCatalog == null)
                {
                    continue;
                }

                var modelEntry = new CacheTypes.CacheModelEntry
                {
                    ModelId = modelLibraryCatalog.ModelId,
                    Versions = new Dictionary<string, CacheTypes.CacheModelVersionEntry>()
                };
                cacheCatalog.Models.Add(rootCatalogEntry.ModelName, modelEntry);

                foreach (var modelLibraryCatalogEntry in modelLibraryCatalog.MajorVersions)
                {
                    var versionsCatalog = await ReadWithNotFoundRetryAsync(() =>
                        GetModelLibraryVersionsCatalogAsync(
                            rootCatalogEntry.ModelName,
                            modelLibraryCatalogEntry.MajorVersion)).ConfigureAwait(false);

                    if (versionsCatalog == null)
                    {
                        continue;
                    }

                    foreach (var modelLibraryVersionsCatalogEntry in versionsCatalog.Versions)
                    {
                        var ckVersion = new CkVersion(modelLibraryVersionsCatalogEntry.Version);
                        if (!modelEntry.Versions.ContainsKey(ckVersion.ToString()))
                        {
                            modelEntry.Versions.Add(ckVersion.ToString(), new CacheTypes.CacheModelVersionEntry
                            {
                                Version = ckVersion,
                                Description = versionsCatalog.Description,
                                FilePath = modelLibraryVersionsCatalogEntry.FilePath
                            });
                        }
                    }
                }
            }
        }

        await WriteCacheAsync(cacheCatalog).ConfigureAwait(false);
    }


    private async Task<(SharedCatalogTypes.RootCatalog? Catalog, bool SourceUnreachable)>
        GetRootCatalogWithReachabilityAsync()
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                var httpResponse = await httpClient.GetAsync(catalogPath, CancellationToken.None)
                    .ConfigureAwait(false);
                if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // The source responded: the catalog simply does not exist (empty catalog)
                    return (null, false);
                }

                if (!httpResponse.IsSuccessStatusCode)
                {
                    // Server error — the source of truth is unavailable.
                    return (null, true);
                }

                response = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Network-level failure — the source of truth is unreachable.
                // The refresh still writes a cache, but flags it so that a "model not found"
                // answer is not mistaken for "model does not exist" (silent-failure guard).
                return (null, true);
            }
            catch (TaskCanceledException)
            {
                // Request timeout — treat like an unreachable source
                return (null, true);
            }
        }
        else
        {
            // The Octokit path lets network failures propagate loudly; GetFileAsync only
            // returns null for a missing file (404), which means "empty catalog".
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return (null, false);
            }

            response = r.Value.Item1;
        }

        if (string.IsNullOrEmpty(response) || response == null)
        {
            return (null, false);
        }

        return (System.Text.Json.JsonSerializer.Deserialize<SharedCatalogTypes.RootCatalog>(
            response,
            new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }), false);
    }

    private async Task<SharedCatalogTypes.ModelLibraryCatalog?> GetModelLibraryCatalogAsync(string catalogPath)
    {
        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Catalog doesn't exist or couldn't be fetched
                return null;
            }
        }
        else
        {
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return null;
            }

            response = r.Value.Item1;
        }

        if (!string.IsNullOrWhiteSpace(response) && response != null)
        {
            var modelLibraryCatalog = System.Text.Json.JsonSerializer
                .Deserialize<SharedCatalogTypes.ModelLibraryCatalog>(
                    response,
                    new System.Text.Json.JsonSerializerOptions
                        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            return modelLibraryCatalog;
        }

        return null;
    }

    // See the API-vs-gh-pages note on UpdateModelVersionsCatalogAsync.
    private static async Task UpdateRootCatalogAsync(CkModelId modelId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update model catalog for {modelId.Name}",
                existingJson => MergeRootCatalog(existingJson, modelId))
            .ConfigureAwait(false);
    }

    internal static string? MergeRootCatalog(string? existingJson, CkModelId modelId)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.RootCatalog>(existingJson);

        catalogData ??= new SharedCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Models = []
        };

        var existingEntry = catalogData.Models.FirstOrDefault(m => m.ModelName == modelId.Name);
        if (existingEntry != null)
        {
            return null;
        }

        catalogData.Models.Add(new SharedCatalogTypes.RootCatalogEntry
        {
            ModelName = modelId.Name,
            CatalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}"
        });

        // Sort models alphabetically
        catalogData.Models = catalogData.Models.OrderBy(m => m.ModelName).ToList();
        catalogData.UpdatedAt = DateTime.UtcNow;

        return SerializeCatalog(catalogData);
    }

    // See the API-vs-gh-pages note on UpdateModelVersionsCatalogAsync.
    private static async Task UpdateModelLibraryCatalogAsync(CkModelId modelId, IGitHubClientWrapper gitHubClient)
    {
        // Catalog file path for the model
        var catalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update model catalog for {modelId.Name}",
                existingJson => MergeModelLibraryCatalog(existingJson, modelId))
            .ConfigureAwait(false);
    }

    internal static string? MergeModelLibraryCatalog(string? existingJson, CkModelId modelId)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.ModelLibraryCatalog>(existingJson);

        catalogData ??= new SharedCatalogTypes.ModelLibraryCatalog
        {
            ModelId = modelId.Name,
            MajorVersions = new List<SharedCatalogTypes.ModelLibraryCatalogEntry>()
        };

        var currentMajor = modelId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);
        if (majorVersionEntry != null)
        {
            return null;
        }

        catalogData.MajorVersions.Add(new SharedCatalogTypes.ModelLibraryCatalogEntry
        {
            MajorVersion = currentMajor,
            CatalogPath =
                $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{currentMajor}/{CatalogFileName}"
        });

        catalogData.UpdatedAt = DateTime.UtcNow;

        return SerializeCatalog(catalogData);
    }
}