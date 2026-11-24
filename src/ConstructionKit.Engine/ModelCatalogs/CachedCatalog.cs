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
    CatalogOptions catalogOptions) : ICatalog, ICancellableCatalogRefresh
{
    /// <inheritdoc />
    public int Order { get; } = order;

    /// <inheritdoc />
    public string CatalogName { get; } = catalogName;

    /// <inheritdoc />
    public string Description { get; } = description;

    /// <inheritdoc />
    /// <remarks>
    ///     Virtual so that catalogs whose enabled state can change at runtime (e.g.
    ///     <see cref="LocalFileSystemCatalog" />, toggled via the CLI <c>-lce</c> switch after the
    ///     singleton has been constructed) can evaluate it live instead of freezing the constructor value.
    /// </remarks>
    public virtual bool CanWrite { get; } = canWrite;

    /// <inheritdoc />
    /// <remarks>
    ///     Virtual — see <see cref="CanWrite" />.
    /// </remarks>
    public virtual bool CanRead { get; } = canRead;

    /// <inheritdoc />
    public abstract Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false);

    /// <inheritdoc />
    /// <remarks>
    ///     The default does not start a refresh that is canceled already and otherwise runs the refresh without the
    ///     token; catalogs that read a remote source override it (AB#9388).
    /// </remarks>
    public virtual Task RefreshCatalogAsync(object? sourceIdentifier, bool forceRefresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RefreshCatalogAsync(sourceIdentifier, forceRefresh);
    }

    /// <inheritdoc />
    public abstract bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <inheritdoc />
    /// <remarks>
    ///     Answered from the cache file. Virtual so that a catalog whose source is cheap to read directly
    ///     (<see cref="LocalFileSystemCatalog" />) can answer from the source of truth instead — the cache
    ///     can be stale when another process publishes concurrently (AB#5661).
    /// </remarks>
    public virtual async Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
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
    /// <remarks>Virtual — see <see cref="IsExistingAsync(CkModelIdVersionRange, object?)" />.</remarks>
    public virtual async Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
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

        // A partial cache cannot say "does not exist" (AB#9388).
        ThrowIfPartial(catalog);
        return false;
    }

    /// <summary>
    ///     AB#9388: a cache that the last refresh read only partly is not answered as a complete inventory (OCTO-CK202).
    ///     An empty flagged cache - the source was not reachable at all - keeps answering from what is there.
    /// </summary>
    /// <remarks>
    ///     A catalog that can ask its repository is exempt: its readers that need a complete answer (dependency
    ///     resolution, baseline, cascade) complete the cache answer from the source of truth (AB#6588).
    /// </remarks>
    private void ThrowIfPartial(CacheTypes.CacheCatalog catalog)
    {
        if (catalog.Partial && this is not IAuthoritativeVersionCatalog { IsAuthoritativeReadAvailable: true })
        {
            throw ModelCatalogException.PartialCache(CatalogName);
        }
    }

    /// <inheritdoc />
    public abstract Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task<CatalogUnpublishResult> UnpublishAsync(CkModelId modelId, string? reason = null,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);
        ThrowIfPartial(catalog);

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
        ThrowIfPartial(catalog);

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

                // AB#9305: read the file into memory and close it before parsing - an open handle blocks a writer's
                // rename on Windows, so it is held as briefly as possible.
                using var content = await CatalogFileIo.ReadSharedAsync(cachePath).ConfigureAwait(false);
                var r = await System.Text.Json.JsonSerializer
                    .DeserializeAsync<CacheTypes.CacheCatalog>(content,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                            Converters = { new CkVersionConverter() }
                        }).ConfigureAwait(false);

                return r ?? new CacheTypes.CacheCatalog();
            }
            catch (Exception ex) when (
                attempt < maxRetries &&
                (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException))
            {
                // Another process may be rewriting the cache file concurrently.
                // FileNotFoundException: file was deleted between exists-check and open
                // IOException: file is locked by another process
                // UnauthorizedAccessException: on Windows, the file is being replaced at this moment (AB#9305)
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
        // Review N3: write a temp file in the cache directory and rename it into place (a parallel reader sees the old
        // or the new cache, never a partial one). AB#9305: parallel CK model builds of one solution write this cache
        // at the same time; the writers are serialized by a lock file and transient rename failures are retried.
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);
        await CatalogFileIo.WriteJsonExclusiveAsync(cachePath, cacheCatalog,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new CkVersionConverter() }
            }, CacheWriteRetryBudget).ConfigureAwait(false);
    }

    /// <summary>
    ///     AB#9305: how long <see cref="WriteCacheAsync" /> retries transient write failures; tests shorten it.
    /// </summary>
    internal TimeSpan CacheWriteRetryBudget { get; set; } = CatalogFileIo.DefaultWriteRetryBudget;
}
