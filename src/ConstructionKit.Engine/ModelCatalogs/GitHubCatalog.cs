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
    gitHubClientFactory, gitHubOptions.Value, 20, "PublicGitHubCatalog", "Public GitHub catalog");

/// <summary>
/// Private catalog on GitHub for construction kit models.
/// Only enabled when a GitHub API token is configured.
/// </summary>
public class PrivateGitHubCatalog(
    ICkJsonSerializer ckJsonSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PrivateGitHubCatalogOptions> gitHubOptions) : GitHubCatalog(ckJsonSerializer, httpClientFactory,
    gitHubClientFactory, gitHubOptions.Value, 15, "PrivateGitHubCatalog",
    "Private GitHub catalog for development and testing",
    !string.IsNullOrWhiteSpace(gitHubOptions.Value.GitHubApiToken));

/// <summary>
/// Construction kit model catalog for GitHub base class
/// </summary>
public abstract class GitHubCatalog : CachedCatalog
{
    private const string RootPath = "ck-models/v2/";
    private const string CatalogFileName = "catalog.json";
    private const int MaxCacheFileAgeSeconds = 60;

    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly GitHubCatalogOptions _gitHubOptions;

    private readonly bool _isGitHubClientAvailable;

    /// <summary>
    /// Creates a new instance of the <see cref="Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs.GitHubCatalog"/> class.
    /// </summary>
    protected GitHubCatalog(ICkJsonSerializer ckJsonSerializer, IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory,
        GitHubCatalogOptions gitHubOptions, int order, string catalogName, string description,
        bool isEnabled = true) : base(order,
        catalogName, description, isEnabled, isEnabled, gitHubOptions)
    {
        _ckJsonSerializer = ckJsonSerializer;
        _httpClientFactory = httpClientFactory;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubOptions = gitHubOptions;

        if (!string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken) &&
            _gitHubOptions.GitHubApiToken != null)
        {
            _isGitHubClientAvailable = true;
        }
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

        if (!_isGitHubClientAvailable)
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
                        .DeserializeCompiledModelRootAsync(stream, "", operationResult).ConfigureAwait(false);
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
            .DeserializeCompiledModelRootAsync(r.Value.Item1, "", operationResult).ConfigureAwait(false);
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

            // Refresh the in-memory catalog
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (ApiValidationException e)
        {
            throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, e);
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
        if (!_isGitHubClientAvailable)
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

    private async Task UpdateModelVersionsCatalogAsync(CkModelId modelId, string? description,
        IGitHubClientWrapper gitHubClient)
    {
        // Create catalog file path for this major version
        var catalogPath =
            $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{CatalogFileName}";

        // Try to load existing catalog first
        var catalogData = await GetModelLibraryVersionsCatalogAsync(modelId.Name, modelId.Version.Major)
            .ConfigureAwait(false);
        bool isModified = false;
        bool isNew = catalogData == null;

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

        if (isModified)
        {
            // Sort versions in descending order (latest first)
            var sortedVersions = versionDict.Values
                .OrderByDescending(v => new CkVersion(v.Version))
                .ToList();

            catalogData.UpdatedAt = DateTime.UtcNow;
            catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

            // Serialize catalog to JSON
            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            // Update or create the catalog file
            if (isNew)
            {
                // Create a new catalog file
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create catalog for {modelId.Name} v{modelId.Version.Major}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!r.HasValue)
                {
                    throw ModelCatalogException.CannotReadExistingModelVersionCatalog(modelId, CatalogName,
                        catalogPath);
                }

                // Update existing catalog
                await gitHubClient.UpdateFileAsync(
                    catalogPath, $"Update catalog for {modelId.Name} v{modelId.Version.Major}", catalogContent,
                    r.Value.Item2).ConfigureAwait(false);
            }
        }
    }


    /// <summary>
    /// Refreshes the catalog file by scanning all model directories
    /// </summary>
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        return RefreshCatalogAsync(false);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh)
    {
        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdatedAsync(maxAge))
        {
            return;
        }

        var cache = await ReadCacheAsync(false).ConfigureAwait(false);
        var catalog = await GetRootCatalogAsync().ConfigureAwait(false);
        if (catalog != null && cache.UpdatedAt != null && cache.UpdatedAt.Value == catalog.UpdatedAt)
        {
            // No changes in the catalog so we can skip the refresh
            return;
        }

        CacheTypes.CacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow
        };

        if (catalog != null)
        {
            foreach (var rootCatalogEntry in catalog.Models)
            {
                var modelLibraryCatalog =
                    await GetModelLibraryCatalogAsync(rootCatalogEntry.CatalogPath).ConfigureAwait(false);

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
                    var versionsCatalog = await GetModelLibraryVersionsCatalogAsync(
                        rootCatalogEntry.ModelName,
                        modelLibraryCatalogEntry.MajorVersion).ConfigureAwait(false);

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


    private async Task<SharedCatalogTypes.RootCatalog?> GetRootCatalogAsync()
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        string? response;
        if (!_isGitHubClientAvailable)
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

        if (string.IsNullOrEmpty(response) || response == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedCatalogTypes.RootCatalog>(
            response,
            new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    }

    private async Task<SharedCatalogTypes.ModelLibraryCatalog?> GetModelLibraryCatalogAsync(string catalogPath)
    {
        string? response;
        if (!_isGitHubClientAvailable)
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

    private async Task UpdateRootCatalogAsync(CkModelId modelId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        // Get or create catalog
        var catalogData = await GetRootCatalogAsync().ConfigureAwait(false);
        var isNew = catalogData == null;

        catalogData ??= new SharedCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Models = []
        };

        // Find or create entry for this model
        var existingEntry = catalogData.Models.FirstOrDefault(m => m.ModelName == modelId.Name);

        // Get the model catalog to verify the latest version
        if (existingEntry == null)
        {
            // Add new entry
            var newEntry = new SharedCatalogTypes.RootCatalogEntry
            {
                ModelName = modelId.Name,
                CatalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}"
            };
            catalogData.Models.Add(newEntry);

            // Sort models alphabetically
            catalogData.Models = catalogData.Models.OrderBy(m => m.ModelName).ToList();
            catalogData.UpdatedAt = DateTime.UtcNow;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                // Create a new catalog file
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create model catalog for {modelId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw ModelCatalogException.CannotReadExistingModelLibraryCatalog(modelId, CatalogName,
                        catalogPath);
                }

                // Update existing catalog
                await gitHubClient.UpdateFileAsync(
                        catalogPath,
                        $"Update model catalog for {modelId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task UpdateModelLibraryCatalogAsync(CkModelId modelId, IGitHubClientWrapper gitHubClient)
    {
        // Create catalog file path for the model
        var catalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}";

        // Try to load existing catalog first
        var catalogData = await GetModelLibraryCatalogAsync(catalogPath).ConfigureAwait(false);
        var isNew = catalogData == null;

        catalogData ??= new SharedCatalogTypes.ModelLibraryCatalog
        {
            ModelId = modelId.Name,
            MajorVersions = new List<SharedCatalogTypes.ModelLibraryCatalogEntry>()
        };

        // Check or update the entry for the current major version
        var currentMajor = modelId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);

        if (majorVersionEntry == null)
        {
            catalogData.MajorVersions.Add(new SharedCatalogTypes.ModelLibraryCatalogEntry
            {
                MajorVersion = currentMajor,
                CatalogPath =
                    $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{currentMajor}/{CatalogFileName}"
            });

            catalogData.UpdatedAt = DateTime.UtcNow;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                // Create a new catalog file
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create model catalog for {modelId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw ModelCatalogException.CannotReadExistingModelLibraryCatalog(modelId, CatalogName,
                        catalogPath);
                }

                // Update existing catalog
                await gitHubClient.UpdateFileAsync(
                        catalogPath,
                        $"Update model catalog for {modelId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }
}