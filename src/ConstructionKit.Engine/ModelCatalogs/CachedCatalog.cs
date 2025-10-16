using JetBrains.Annotations;
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
/// <param name="catalogOptions">Options for the catalog</param>
public abstract class CachedCatalog(
    int order,
    string catalogName,
    string description,
    bool canWrite,
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
    public abstract Task RefreshCatalogAsync();

    /// <inheritdoc />
    public abstract bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <inheritdoc />
    public async Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        var catalog = await ReadCacheAsync().ConfigureAwait(false);

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
                        ModelId = candidateVersions.OrderBy(v => v).Last()
                    };
                }

                break;
            }
        }

        return new ModelExistingResult
        {
            Exists = false,
            ModelId = null
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        var catalog = await ReadCacheAsync().ConfigureAwait(false);

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
        var catalog = await ReadCacheAsync().ConfigureAwait(false);

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
        searchTerm = searchTerm?.Trim() ?? string.Empty;
        var catalog = await ReadCacheAsync().ConfigureAwait(false);

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
    /// </summary>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the cache catalog, or null if the cache file does not exist.</returns>
    // ReSharper disable once MemberCanBePrivate.Global
    protected async Task<CacheTypes.CacheCatalog> ReadCacheAsync()
    {
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);
        if (!File.Exists(cachePath))
        {
            // If the cache file does not exist, refresh the catalog to create it
            await RefreshCatalogAsync().ConfigureAwait(false);
            if (!File.Exists(cachePath))
            {
                return new CacheTypes.CacheCatalog();
            }
        }

        using var streamReader = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,  // Allow other processes to read/write while we read
            bufferSize: 4096,
            useAsync: true
        );
        var r = await System.Text.Json.JsonSerializer
            .DeserializeAsync<CacheTypes.CacheCatalog>(streamReader,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new CkVersionConverter() }
                }).ConfigureAwait(false);

        return r ?? new CacheTypes.CacheCatalog();
    }

    /// <summary>
    /// Writes the given cache catalog to the configured cache file as JSON.
    /// </summary>
    /// <param name="cacheCatalog">The cache catalog to write.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected async Task WriteCacheAsync(CacheTypes.CacheCatalog cacheCatalog)
    {
        var tempFileName = Path.GetTempFileName();

        using var streamWriter = File.Create(tempFileName);
        await System.Text.Json.JsonSerializer
            .SerializeAsync(streamWriter, cacheCatalog,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new CkVersionConverter() }
                }).ConfigureAwait(false);

        if (!Directory.Exists(catalogOptions.CacheDirectory))
        {
            Directory.CreateDirectory(catalogOptions.CacheDirectory);
        }
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }
        File.Move(tempFileName, cachePath);
    }
}