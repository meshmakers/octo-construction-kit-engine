using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// A base class for cached blueprint catalogs that implements common functionality for reading and writing a cache file.
/// Derived classes must implement methods for refreshing the catalog and checking support for source identifiers.
/// </summary>
/// <param name="order">Order of the catalog</param>
/// <param name="catalogName">Name of the catalog</param>
/// <param name="description">Description of the catalog</param>
/// <param name="canWrite">When true, the catalog can be used to publish or update blueprints</param>
/// <param name="canRead">When true, the catalog is enabled and read or write operations are possible</param>
/// <param name="catalogOptions">Options for the catalog</param>
public abstract class CachedBlueprintCatalog(
    int order,
    string catalogName,
    string description,
    bool canWrite,
    bool canRead,
    BlueprintCatalogOptions catalogOptions) : IBlueprintCatalog
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
    public abstract Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false);

    /// <inheritdoc />
    public abstract bool IsSupportingSourceIdentifier(object? sourceIdentifier = null);

    /// <inheritdoc />
    public async Task<BlueprintExistingResult> IsExistingAsync(BlueprintIdVersionRange blueprintIdVersionRange,
        object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheBlueprintEntry in catalog.Blueprints.Values)
        {
            if (cacheBlueprintEntry.BlueprintName == blueprintIdVersionRange.Name)
            {
                List<BlueprintId> candidateVersions = [];
                foreach (var cacheVersionEntry in cacheBlueprintEntry.Versions.Values)
                {
                    if (blueprintIdVersionRange.BlueprintVersionRange.IsSatisfiedBy(cacheVersionEntry.Version))
                    {
                        candidateVersions.Add(new BlueprintId(cacheBlueprintEntry.BlueprintName, cacheVersionEntry.Version));
                    }
                }

                if (candidateVersions.Count > 0)
                {
                    return new BlueprintExistingResult
                    {
                        Exists = true,
                        BlueprintId = candidateVersions.OrderBy(v => v).Last()
                    };
                }

                break;
            }
        }

        return new BlueprintExistingResult
        {
            Exists = false,
            BlueprintId = null
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsExistingAsync(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheBlueprintEntry in catalog.Blueprints.Values)
        {
            foreach (var cacheVersionEntry in cacheBlueprintEntry.Versions.Values)
            {
                if (cacheBlueprintEntry.BlueprintName == blueprintId.Name && cacheVersionEntry.Version == blueprintId.Version)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public abstract Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member — the base interface is intentional.
    [Obsolete("Use OpenBlueprintFileAsync.")]
    public abstract string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null);
#pragma warning restore CS0809

    /// <summary>
    ///     Validates that <paramref name="relativePath" /> stays inside the blueprint root.
    ///     Throws <see cref="BlueprintCatalogException" /> on any traversal / rooted / empty path.
    ///     Thin wrapper around <see cref="BlueprintRelativePath.Validate" /> kept here for the
    ///     benefit of derived classes; embedded / non-cached catalogs call the static directly.
    /// </summary>
    /// <param name="relativePath">The candidate relative path</param>
    /// <returns>The normalised path (forward slashes, no leading separators).</returns>
    protected static string ValidateBlueprintRelativePath(string relativePath)
        => BlueprintRelativePath.Validate(relativePath);

    /// <inheritdoc />
    public abstract Task PublishAsync(BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task UnpublishAsync(BlueprintId blueprintId, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public abstract Task UnpublishAllVersionsAsync(string blueprintName, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null);

    /// <inheritdoc />
    public async IAsyncEnumerable<BlueprintCatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheBlueprintEntry in catalog.Blueprints.Values)
        {
            foreach (var cacheVersionEntry in cacheBlueprintEntry.Versions.Values)
            {
                yield return new BlueprintCatalogResultItem
                {
                    CatalogName = CatalogName,
                    BlueprintId = new BlueprintId(cacheBlueprintEntry.BlueprintName, cacheVersionEntry.Version),
                    Description = cacheVersionEntry.Description
                };
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BlueprintCatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        searchTerm = searchTerm?.Trim() ?? string.Empty;
        var catalog = await ReadCacheAsync(true).ConfigureAwait(false);

        foreach (var cacheBlueprintEntry in catalog.Blueprints.Values)
        {
            foreach (var cacheVersionEntry in cacheBlueprintEntry.Versions.Values)
            {
                if (cacheBlueprintEntry.BlueprintName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    (cacheVersionEntry.Description != null &&
                     cacheVersionEntry.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return new BlueprintCatalogResultItem
                    {
                        CatalogName = CatalogName,
                        BlueprintId = new BlueprintId(cacheBlueprintEntry.BlueprintName, cacheVersionEntry.Version),
                        Description = cacheVersionEntry.Description
                    };
                }
            }
        }
    }

    /// <summary>
    /// Reads the cache catalog from the configured cache file if it exists.
    /// </summary>
    /// <param name="createCacheIfNotExists">Create cache file if cache does not exist.</param>
    /// <returns>A task that represents the asynchronous read operation. The task result contains the cache catalog.</returns>
    protected async Task<BlueprintCacheTypes.BlueprintCacheCatalog> ReadCacheAsync(bool createCacheIfNotExists)
    {
        var cachePath = Path.Combine(catalogOptions.CacheDirectory, catalogOptions.CacheFileName);
        if (!File.Exists(cachePath))
        {
            if (createCacheIfNotExists)
            {
                try
                {
                    await RefreshCatalogAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Cache bootstrap is best-effort: read paths served an empty catalog on fetch
                    // failures before refresh started propagating transport errors (AB#4309), and
                    // they must keep doing so — only explicit refreshes report the failure.
                }
            }

            if (!File.Exists(cachePath))
            {
                return new BlueprintCacheTypes.BlueprintCacheCatalog();
            }
        }

#if NETSTANDARD2_0
        using var streamReader = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true
        );
#else
        await using var streamReader = new FileStream(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true
        );
#endif

        var result = await System.Text.Json.JsonSerializer
            .DeserializeAsync<BlueprintCacheTypes.BlueprintCacheCatalog>(streamReader,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    Converters = { new CkVersionConverter() }
                }).ConfigureAwait(false);
        streamReader.Close();

        return result ?? new BlueprintCacheTypes.BlueprintCacheCatalog();
    }

    /// <summary>
    /// Returns true if the cache file was updated within the given max age.
    /// </summary>
    /// <param name="maxAge">The maximum age of the cache file.</param>
    /// <returns>True if the cache file was updated within the given max age; otherwise, false.</returns>
    protected bool IsCacheFileRecentlyUpdated(TimeSpan maxAge)
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
    protected async Task WriteCacheAsync(BlueprintCacheTypes.BlueprintCacheCatalog cacheCatalog)
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

        const int maxRetries = 5;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
                File.Move(tempFileName, cachePath);
                break;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }
}
