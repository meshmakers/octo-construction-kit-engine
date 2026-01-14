using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Public catalog on GitHub for blueprints
/// </summary>
public class PublicGitHubBlueprintCatalog(
    IBlueprintSerializer blueprintSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PublicGitHubBlueprintCatalogOptions> gitHubOptions) : GitHubBlueprintCatalog(blueprintSerializer,
    httpClientFactory,
    gitHubClientFactory,
    gitHubOptions.Value, 20, "PublicGitHubBlueprintCatalog", "Public GitHub blueprint catalog");

/// <summary>
/// Private catalog on GitHub for blueprints
/// </summary>
public class PrivateGitHubBlueprintCatalog(
    IBlueprintSerializer blueprintSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PrivateGitHubBlueprintCatalogOptions> gitHubOptions) : GitHubBlueprintCatalog(blueprintSerializer,
    httpClientFactory,
    gitHubClientFactory,
    gitHubOptions.Value, 21, "PrivateGitHubBlueprintCatalog",
    "Private GitHub blueprint catalog for development and testing");

/// <summary>
/// Blueprint catalog for GitHub base class.
/// Provides access to blueprints hosted on GitHub.
/// </summary>
public abstract class GitHubBlueprintCatalog : CachedBlueprintCatalog
{
    private const string RootPath = "blueprints/v1/";
    private const string CatalogFileName = "catalog.json";
    private const string BlueprintMetaFileName = "blueprint.yaml";
    private const int MaxCacheFileAgeSeconds = 60;

    private readonly IBlueprintSerializer _blueprintSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly GitHubBlueprintCatalogOptions _gitHubOptions;
    private readonly bool _isGitHubClientAvailable;

    /// <summary>
    /// Creates a new instance of the <see cref="GitHubBlueprintCatalog"/> class.
    /// </summary>
    protected GitHubBlueprintCatalog(
        IBlueprintSerializer blueprintSerializer,
        IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory,
        GitHubBlueprintCatalogOptions gitHubOptions,
        int order,
        string catalogName,
        string description) : base(order, catalogName, description, true, true, gitHubOptions)
    {
        _blueprintSerializer = blueprintSerializer;
        _httpClientFactory = httpClientFactory;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubOptions = gitHubOptions;

        _isGitHubClientAvailable = !string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken);
    }

    /// <inheritdoc />
    public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public override async Task<BlueprintMetaRootDto> GetAsync(
        BlueprintId blueprintId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var blueprintPath = CreateBlueprintMetaPath(blueprintId);
        var httpClient = CreateHttpClient();

        try
        {
            var response = await httpClient.GetAsync(blueprintPath, cancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
#if NETSTANDARD2_0
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                var blueprintMeta = await _blueprintSerializer
                    .DeserializeBlueprintMetaAsync(stream, blueprintPath, operationResult)
                    .ConfigureAwait(false);

                if (operationResult.HasErrors)
                {
                    throw BlueprintCatalogException.ErrorDuringBlueprintLoad(blueprintId, CatalogName, operationResult);
                }

                return blueprintMeta;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw BlueprintCatalogException.BlueprintNotFound(blueprintId);
            }

            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
        catch (HttpRequestException)
        {
            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
        catch (TaskCanceledException)
        {
            throw BlueprintCatalogException.RequestTimeout(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
    }

    /// <inheritdoc />
    public override string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');
        return $"{baseUri}/{CreateBlueprintDirectoryPath(blueprintId)}";
    }

    /// <inheritdoc />
    public override async Task PublishAsync(
        BlueprintMetaRootDto blueprintMetaRoot,
        string blueprintDirectory,
        bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();
        var blueprintId = blueprintMetaRoot.BlueprintId;

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            // Upload all blueprint files to GitHub
            await UploadBlueprintFilesAsync(blueprintId, blueprintDirectory, force, gitHubClient, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the major version catalog
            await UpdateBlueprintVersionsCatalogAsync(blueprintId, blueprintMetaRoot.Description, gitHubClient)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the overall blueprint library catalog
            await UpdateBlueprintLibraryCatalogAsync(blueprintId, gitHubClient)
                .ConfigureAwait(false);

            // Update the root catalog
            await UpdateRootCatalogAsync(blueprintId, gitHubClient).ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Refresh the in-memory catalog
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (ApiValidationException e)
        {
            throw BlueprintCatalogException.PublishFailed(blueprintId, CatalogName, e);
        }
    }

    private async Task UploadBlueprintFilesAsync(
        BlueprintId blueprintId,
        string blueprintDirectory,
        bool force,
        IGitHubClientWrapper gitHubClient,
        CancellationToken? cancellationToken)
    {
        // Get all files in the blueprint directory
        var files = Directory.GetFiles(blueprintDirectory, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            var relativePath = GetRelativePath(blueprintDirectory, filePath);
            var gitHubPath = $"{CreateBlueprintDirectoryPath(blueprintId)}/{relativePath.Replace('\\', '/')}";

#if NETSTANDARD2_0
            var content = File.ReadAllText(filePath);
#else
            var content = await File.ReadAllTextAsync(filePath, cancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);
#endif

            var existing = await gitHubClient.GetFileAsync(gitHubPath).ConfigureAwait(false);
            if (existing.HasValue)
            {
                if (!force)
                {
                    throw BlueprintCatalogException.BlueprintAlreadyExistsInCatalog(blueprintId, CatalogName);
                }

                await gitHubClient.UpdateFileAsync(gitHubPath, $"Update {relativePath} for {blueprintId.FullName}",
                        content, existing.Value.Item2)
                    .ConfigureAwait(false);
            }
            else
            {
                await gitHubClient.CreateFileAsync(gitHubPath, $"Add {relativePath} for {blueprintId.FullName}", content)
                    .ConfigureAwait(false);
            }
        }
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
#if NETSTANDARD2_0
        // Manual implementation for netstandard2.0
        var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? basePath
            : basePath + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
#else
        return Path.GetRelativePath(basePath, fullPath);
#endif
    }

    private IGitHubClientWrapper CreateGitHubClient()
    {
        if (!_isGitHubClientAvailable)
        {
            throw BlueprintCatalogException.GitHubTokenMissing();
        }

        return _gitHubClientFactory.CreateClient(_gitHubOptions);
    }

    private async Task UpdateBlueprintVersionsCatalogAsync(
        BlueprintId blueprintId,
        string? description,
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath =
            $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{blueprintId.Version.Major}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryVersionsCatalogWithClientAsync(
                blueprintId.Name, blueprintId.Version.Major, gitHubClient)
            .ConfigureAwait(false);

        var isNew = catalogData == null;
        var isModified = false;

        catalogData ??= new SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog
        {
            BlueprintId = blueprintId.Name,
            MajorVersion = blueprintId.Version.Major,
            Versions = []
        };

        if (catalogData.Description != description)
        {
            isModified = true;
            catalogData.Description = description;
        }

        var versionDict = catalogData.Versions.ToDictionary(k => k.Version, v => v);
        var currentVersionString = blueprintId.Version.ToString();

        if (!versionDict.ContainsKey(currentVersionString))
        {
            var directoryPath = CreateBlueprintDirectoryPath(blueprintId);

            versionDict[currentVersionString] = new SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalogEntry
            {
                Version = currentVersionString,
                DirectoryPath = directoryPath,
                PublishedAt = DateTime.UtcNow
            };

            catalogData.Versions.Clear();
            catalogData.Versions.AddRange(versionDict.Values.OrderBy(v => v.Version));
            isModified = true;
        }

        if (isModified)
        {
            var sortedVersions = versionDict.Values
                .OrderByDescending(v => new CkVersion(v.Version))
                .ToList();

            catalogData.UpdatedAt = DateTime.UtcNow;
            catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                        catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!r.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingVersionsCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                    catalogPath, $"Update catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                    catalogContent, r.Value.Item2).ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog?> GetBlueprintLibraryVersionsCatalogWithClientAsync(
        string blueprintName,
        int majorVersion,
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath =
            $"{RootPath}{blueprintName[0].ToString().ToLower()}/{blueprintName}/{majorVersion}/{CatalogFileName}";

        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task UpdateBlueprintLibraryCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryCatalogWithClientAsync(catalogPath, gitHubClient)
            .ConfigureAwait(false);

        var isNew = catalogData == null;

        catalogData ??= new SharedBlueprintCatalogTypes.BlueprintLibraryCatalog
        {
            BlueprintId = blueprintId.Name,
            MajorVersions = []
        };

        var currentMajor = blueprintId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);

        if (majorVersionEntry == null)
        {
            catalogData.MajorVersions.Add(new SharedBlueprintCatalogTypes.BlueprintLibraryCatalogEntry
            {
                MajorVersion = currentMajor,
                CatalogPath =
                    $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{currentMajor}/{CatalogFileName}"
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
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create blueprint catalog for {blueprintId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingLibraryCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                        catalogPath, $"Update blueprint catalog for {blueprintId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog?> GetBlueprintLibraryCatalogWithClientAsync(
        string catalogPath,
        IGitHubClientWrapper gitHubClient)
    {
        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task UpdateRootCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        var catalogData = await GetRootCatalogWithClientAsync(gitHubClient).ConfigureAwait(false);
        var isNew = catalogData == null;

        catalogData ??= new SharedBlueprintCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Blueprints = []
        };

        var existingEntry = catalogData.Blueprints.FirstOrDefault(m => m.BlueprintName == blueprintId.Name);

        if (existingEntry == null)
        {
            var newEntry = new SharedBlueprintCatalogTypes.RootCatalogEntry
            {
                BlueprintName = blueprintId.Name,
                CatalogPath =
                    $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{CatalogFileName}"
            };
            catalogData.Blueprints.Add(newEntry);

            catalogData.Blueprints = catalogData.Blueprints.OrderBy(m => m.BlueprintName).ToList();
            catalogData.UpdatedAt = DateTime.UtcNow;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create blueprint catalog for {blueprintId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingLibraryCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                        catalogPath, $"Update blueprint catalog for {blueprintId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.RootCatalog?> GetRootCatalogWithClientAsync(
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.RootCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    /// <inheritdoc />
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        return RefreshCatalogAsync(false);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh)
    {
        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdated(maxAge))
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

        BlueprintCacheTypes.BlueprintCacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow
        };

        if (catalog != null)
        {
            foreach (var rootCatalogEntry in catalog.Blueprints)
            {
                var blueprintLibraryCatalog = await GetBlueprintLibraryCatalogAsync(rootCatalogEntry.CatalogPath)
                    .ConfigureAwait(false);

                if (blueprintLibraryCatalog == null)
                {
                    continue;
                }

                var blueprintEntry = new BlueprintCacheTypes.BlueprintCacheEntry
                {
                    BlueprintName = blueprintLibraryCatalog.BlueprintId,
                    Versions = new Dictionary<string, BlueprintCacheTypes.BlueprintCacheVersionEntry>()
                };
                cacheCatalog.Blueprints.Add(rootCatalogEntry.BlueprintName, blueprintEntry);

                foreach (var majorVersionEntry in blueprintLibraryCatalog.MajorVersions)
                {
                    var versionsCatalog = await GetBlueprintLibraryVersionsCatalogAsync(
                        rootCatalogEntry.BlueprintName,
                        majorVersionEntry.MajorVersion).ConfigureAwait(false);

                    if (versionsCatalog == null)
                    {
                        continue;
                    }

                    foreach (var versionEntry in versionsCatalog.Versions)
                    {
                        var ckVersion = new CkVersion(versionEntry.Version);
                        if (!blueprintEntry.Versions.ContainsKey(ckVersion.ToString()))
                        {
                            blueprintEntry.Versions.Add(ckVersion.ToString(), new BlueprintCacheTypes.BlueprintCacheVersionEntry
                            {
                                Version = ckVersion,
                                Description = versionsCatalog.Description,
                                DirectoryPath = versionEntry.DirectoryPath
                            });
                        }
                    }
                }
            }
        }

        await WriteCacheAsync(cacheCatalog).ConfigureAwait(false);
    }

    private string CreateBlueprintMetaPath(BlueprintId blueprintId)
    {
        return $"{CreateBlueprintDirectoryPath(blueprintId)}/{BlueprintMetaFileName}";
    }

    private string CreateBlueprintDirectoryPath(BlueprintId blueprintId)
    {
        return RootPath
               + blueprintId.Name[0].ToString().ToLower() + "/"
               + blueprintId.Name + "/"
               + blueprintId.Version.Major + "/"
               + blueprintId.FullName;
    }

    private IHttpClientWrapper CreateHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri))
        {
            throw BlueprintCatalogException.GitHubPagesUriMissing();
        }

        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');
        return _httpClientFactory.CreateClient(new Uri($"{baseUri}"));
    }

    private async Task<SharedBlueprintCatalogTypes.RootCatalog?> GetRootCatalogAsync()
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";
        var httpClient = CreateHttpClient();

        try
        {
            var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response))
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.RootCatalog>(
                response!,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
        }
        catch (HttpRequestException)
        {
            // Catalog doesn't exist or couldn't be fetched
            return null;
        }
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog?> GetBlueprintLibraryCatalogAsync(
        string catalogPath)
    {
        var httpClient = CreateHttpClient();

        try
        {
            var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(response))
            {
                return System.Text.Json.JsonSerializer
                    .Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog>(
                        response!,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                        });
            }
        }
        catch (HttpRequestException)
        {
            // Catalog doesn't exist or couldn't be fetched
        }

        return null;
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog?> GetBlueprintLibraryVersionsCatalogAsync(
        string blueprintName,
        int majorVersion)
    {
        var catalogPath = $"{RootPath}{blueprintName[0].ToString().ToLower()}/{blueprintName}/{majorVersion}/{CatalogFileName}";
        var httpClient = CreateHttpClient();

        try
        {
            var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(response))
            {
                return System.Text.Json.JsonSerializer
                    .Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog>(
                        response!,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                        });
            }
        }
        catch (HttpRequestException)
        {
            // Catalog doesn't exist or couldn't be fetched
        }

        return null;
    }
}
