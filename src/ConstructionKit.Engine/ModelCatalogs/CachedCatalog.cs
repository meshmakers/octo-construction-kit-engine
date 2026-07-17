using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// A base class for cached catalogs that implements common functionality for reading and writing a cache file.
/// Derived classes must implement methods for refreshing the catalog, checking support for source identifiers,
/// </summary>
/// <param name="order">Order of the catalog</param>
/// <param name="catalogName">Name of the catalog</param>
/// <param name="description">Description of the catalog</param>
/// <param name="canWrite">When true, the catalog can be used to publish or update models</param>
/// <param name="canRead">When true, the catalog is enabled and read or write operations are possible</param>
/// <param name="catalogOptions">Options for the catalog</param>
public abstract class CachedCatalog(
    int order,
    string catalogName,
    string description,
    bool canWrite,
    bool canRead,
    CatalogOptions catalogOptions) : ICatalog
{
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public string CatalogName { get; } = catalogName;

    /// <inheritdoc />
    public string Description { get; } = description;

    /// <inheritdoc />
    public bool CanWrite { get; } = canWrite;

    /// <inheritdoc />
    public bool CanRead { get; } = canRead;

    /// <inheritdoc />
    public abstract Task RefreshCatalogAsync(object? sourceIdentifier = null);

    /// <inheritdoc />
    public abstract bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <inheritdoc />
    public async Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheModelEntry in catalog.Models.Values)
        {
            if (cacheModelEntry.ModelId == modelIdVersionRange.Name)
            {
                List<CkModelId> candidateVersions = new();
                foreach (var cacheModelVersionEntry in cacheModelEntry.Versions.Values)
                {
                    if (modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(cacheModelVersionEntry.Version))
                    {
                        candidateVersions.Add(new CkModelId(cacheModelEntry.ModelId, cacheModelVersionEntry.Version));
                    }
                }

                if (candidateVersions.Any())
                {
                    return new ModelExistingResult
                    {
                        Exists = true,
                        ModelId = candidateVersions.OrderBy(v => v).Last(),
                        CatalogName = CatalogName,
                        CacheUpdatedAt = catalog.UpdatedAt,
                        SourceUnreachable = catalog.SourceUnreachable
                    };
                }

                break;
            }
        }

        return new ModelExistingResult
        {
            Exists = false,
            ModelId = null,
            CatalogName = CatalogName,
            CacheUpdatedAt = catalog.UpdatedAt,
            SourceUnreachable = catalog.SourceUnreachable
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheModelEntry in catalog.Models.Values)
        {
            foreach (var cacheModelVersionEntry in cacheModelEntry.Versions.Values)
            {
                if (cacheModelEntry.ModelId == modelId.Name && cacheModelVersionEntry.Version == modelId.Version)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public abstract Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheModelEntry in catalog.Models.Values)
        {
            foreach (var cacheModelVersionEntry in cacheModelEntry.Versions.Values)
            {
                yield return new CatalogResultItem
                {
                    CatalogName = CatalogName,
                    ModelId = new CkModelId(cacheModelEntry.ModelId, cacheModelVersionEntry.Version),
                    Description = cacheModelVersionEntry.Description
                };
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        searchTerm = searchTerm?.Trim() ?? string.Empty;
        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheModelEntry in catalog.Models.Values)
        {
            foreach (var cacheModelVersionEntry in cacheModelEntry.Versions.Values)
            {
                if (cacheModelEntry.ModelId.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (cacheModelVersionEntry.Description != null &&
                     cacheModelVersionEntry.Description.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    yield return new CatalogResultItem
                    {
                        CatalogName = CatalogName,
                        ModelId = new CkModelId(cacheModelEntry.ModelId, cacheModelVersionEntry.Version),
                        Description = cacheModelVersionEntry.Description
                    };
                }
            }
        }
    }

    /// <summary>
    /// Reads the cache catalog from the configured cache file if it exists.
    /// Uses retry logic to handle concurrent access from parallel build processes.
    /// </summary>
    /// <param name="createCacheIfNotExits">Create cache file if cache not exists.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the cache catalog, or null if the cache file does not exist.</returns>
    // ReSharper disable once MemberCanBePrivate.Global
    protected async Task<CacheTypes.CacheCatalog> ReadCacheAsync(bool createCacheIfNotExits)
    {
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);

        if (!File.Exists(cachePath) && createCacheIfNotExits)
        {
            // If the cache file does not exist, refresh the catalog to create it
            await RefreshCatalogAsync().ConfigureAwait(false);
        }

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (!File.Exists(cachePath))
                {
                    return new CacheTypes.CacheCatalog();
                }

#if NETSTANDARD2_0
                using var streamReader = new FileStream(
                    cachePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite, // Allow other processes to read/write while we read
                    bufferSize: 4096,
                    useAsync: true
                );
#else
                await using var streamReader = new FileStream(
                    cachePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite, // Allow other processes to read/write while we read
                    bufferSize: 4096,
                    useAsync: true
                );
#endif

                var r = await System.Text.Json.JsonSerializer
                    .DeserializeAsync<CacheTypes.CacheCatalog>(streamReader,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            Converters = { new CkVersionConverter() }
                        }).ConfigureAwait(false);
                streamReader.Close();

                return r ?? new CacheTypes.CacheCatalog();
            }
            catch (Exception ex) when (
                attempt < maxRetries &&
                (ex is FileNotFoundException || ex is IOException || ex is System.Text.Json.JsonException))
            {
                // Another process may be rewriting the cache file concurrently.
                // FileNotFoundException: file was deleted between exists-check and open
                // IOException: file is locked by another process
                // JsonException: file contains partial/corrupt content during a concurrent write
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        return new CacheTypes.CacheCatalog();
    }

    /// <summary>
    /// Returns true if the cache file was updated within the given max age.
    /// </summary>
    /// <param name="maxAge">The maximum age of the cache file.</param>
    /// <returns>True if the cache file was updated within the given max age; otherwise, false.</returns>
    protected bool IsCacheFileRecentlyUpdatedAsync(TimeSpan maxAge)
    {
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);
        if (!File.Exists(cachePath))
        {
            return false;
        }

        var lastWriteTime = File.GetLastWriteTimeUtc(cachePath);
        return DateTime.UtcNow - lastWriteTime <= maxAge;
    }

    /// <summary>
    /// Writes the given cache catalog to the configured cache file as JSON.
    /// </summary>
    /// <param name="cacheCatalog">The cache catalog to write.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task WriteCacheAsync(CacheTypes.CacheCatalog cacheCatalog)
    {
        var tempFileName = Path.GetTempFileName();

#if NETSTANDARD2_0
        using var streamWriter = File.Create(tempFileName);
#else
        await using var streamWriter = File.Create(tempFileName);
#endif
        await System.Text.Json.JsonSerializer
            .SerializeAsync(streamWriter, cacheCatalog,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new CkVersionConverter() }
                }).ConfigureAwait(false);
        streamWriter.Close();

        if (!Directory.Exists(catalogOptions.CacheDirectory))
        {
            Directory.CreateDirectory(catalogOptions.CacheDirectory);
        }

        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);


        // Use File.Copy with overwrite instead of Delete+Move to avoid a window
        // where the cache file does not exist during concurrent parallel builds.
        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Copy(tempFileName, cachePath, overwrite: true);
                File.Delete(tempFileName);
                break; // Success
            }
            catch (IOException) when (attempt < maxRetries)
            {
                // Wait a bit before retrying
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }
}