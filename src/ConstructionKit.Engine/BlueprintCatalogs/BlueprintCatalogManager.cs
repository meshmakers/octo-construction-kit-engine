using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Manages blueprint catalogs
/// </summary>
internal class BlueprintCatalogManager : IBlueprintCatalogManager
{
    private readonly ILogger<BlueprintCatalogManager> _logger;
    private readonly IEnumerable<IBlueprintCatalog> _catalogs;

    /// <summary>
    /// Creates a new instance of <see cref="BlueprintCatalogManager"/>
    /// </summary>
    /// <param name="logger">Logger</param>
    /// <param name="catalogs">Available catalogs</param>
    public BlueprintCatalogManager(
        ILogger<BlueprintCatalogManager> logger,
        IEnumerable<IBlueprintCatalog> catalogs)
    {
        _logger = logger;
        _catalogs = catalogs;
    }

    /// <inheritdoc />
    public async Task<BlueprintSearchResult> SearchAsync(string searchTerm, int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var allItems = new List<BlueprintCatalogResultItem>();

        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            await foreach (var item in catalog.SearchAsync(searchTerm, sourceIdentifier))
            {
                if (!allItems.Any(i => i.BlueprintId.Equals(item.BlueprintId)))
                {
                    allItems.Add(item);
                }
            }
        }

        return new BlueprintSearchResult
        {
            Items = allItems.Skip(skip).Take(take).ToList(),
            TotalCount = allItems.Count
        };
    }

    /// <inheritdoc />
    public async Task<BlueprintListResult> ListAsync(int skip, int take,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var allItems = new List<BlueprintCatalogResultItem>();

        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            await foreach (var item in catalog.ListAsync(sourceIdentifier))
            {
                if (!allItems.Any(i => i.BlueprintId.Equals(item.BlueprintId)))
                {
                    allItems.Add(item);
                }
            }
        }

        return new BlueprintListResult
        {
            Items = allItems.Skip(skip).Take(take).ToList(),
            TotalCount = allItems.Count
        };
    }

    /// <inheritdoc />
    public async Task<BlueprintMetaRootDto?> TryGetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var exists = await catalog.IsExistingAsync(blueprintId, sourceIdentifier).ConfigureAwait(false);
            if (exists)
            {
                _logger.LogDebug("Blueprint {BlueprintId} found in catalog {CatalogName}", blueprintId, catalog.CatalogName);
                return await catalog.GetAsync(blueprintId, operationResult, sourceIdentifier, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogDebug("Blueprint {BlueprintId} not found in any catalog", blueprintId);
        return null;
    }

    /// <inheritdoc />
    public async Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var result = await TryGetAsync(blueprintId, operationResult, sourceIdentifier, cancellationToken).ConfigureAwait(false);
        if (result == null)
        {
            throw BlueprintCatalogException.BlueprintNotFound(blueprintId);
        }
        return result;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null, CancellationToken cancellationToken = default)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var exists = await catalog.IsExistingAsync(blueprintId, sourceIdentifier).ConfigureAwait(false);
            if (!exists)
            {
                continue;
            }

            return await catalog.OpenBlueprintFileAsync(blueprintId, relativePath, sourceIdentifier,
                cancellationToken).ConfigureAwait(false);
        }

        throw BlueprintCatalogException.BlueprintNotFound(blueprintId);
    }

    /// <inheritdoc />
    public async Task<Stream?> TryOpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null, CancellationToken cancellationToken = default)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var exists = await catalog.IsExistingAsync(blueprintId, sourceIdentifier).ConfigureAwait(false);
            if (!exists)
            {
                continue;
            }

            try
            {
                return await catalog.OpenBlueprintFileAsync(blueprintId, relativePath, sourceIdentifier,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (BlueprintFileNotFoundException)
            {
                // Soft-not-found semantics: try the next catalog (in case a later catalog has a more
                // complete copy of the same blueprint).
                continue;
            }
        }

        return null;
    }

    /// <inheritdoc />
    [Obsolete("Use OpenBlueprintFileAsync.")]
    public async Task<string> GetBlueprintPathAsync(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var exists = await catalog.IsExistingAsync(blueprintId, sourceIdentifier).ConfigureAwait(false);
            if (exists)
            {
#pragma warning disable CS0618 // Forwarding to the deprecated catalog API is intentional here.
                return catalog.GetBlueprintPath(blueprintId, sourceIdentifier);
#pragma warning restore CS0618
            }
        }

        throw BlueprintCatalogException.BlueprintNotFound(blueprintId);
    }

    /// <inheritdoc />
    public IEnumerable<Tuple<string, string>> GetCatalogList(object? sourceIdentifier = null)
    {
        return _catalogs
            .Where(c => c.IsSupportingSourceIdentifier(sourceIdentifier))
            .OrderBy(c => c.Order)
            .Select(c => new Tuple<string, string>(c.CatalogName, c.Description));
    }

    /// <inheritdoc />
    public async Task PublishAsync(string catalogName, BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory,
        bool isForced, object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var catalog = _catalogs.FirstOrDefault(c => c.CatalogName == catalogName);
        if (catalog == null)
        {
            throw BlueprintCatalogException.CatalogNotFound(catalogName);
        }

        if (!catalog.CanWrite)
        {
            throw BlueprintCatalogException.CatalogCannotWrite(catalogName);
        }

        await catalog.PublishAsync(blueprintMetaRoot, blueprintDirectory, isForced, sourceIdentifier, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Published blueprint {BlueprintId} to catalog {CatalogName}",
            blueprintMetaRoot.BlueprintId, catalogName);
    }

    /// <inheritdoc />
    public async Task UnpublishAsync(string catalogName, BlueprintId blueprintId, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var catalog = ResolveWritableCatalog(catalogName);

        await catalog.UnpublishAsync(blueprintId, sourceIdentifier, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Unpublished blueprint {BlueprintId} from catalog {CatalogName}",
            blueprintId, catalogName);
    }

    /// <inheritdoc />
    public async Task UnpublishAllVersionsAsync(string catalogName, string blueprintName, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var catalog = ResolveWritableCatalog(catalogName);

        await catalog.UnpublishAllVersionsAsync(blueprintName, sourceIdentifier, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Unpublished all versions of blueprint {BlueprintName} from catalog {CatalogName}",
            blueprintName, catalogName);
    }

    private IBlueprintCatalog ResolveWritableCatalog(string catalogName)
    {
        var catalog = _catalogs.FirstOrDefault(c => c.CatalogName == catalogName);
        if (catalog == null)
        {
            throw BlueprintCatalogException.CatalogNotFound(catalogName);
        }

        if (!catalog.CanWrite)
        {
            throw BlueprintCatalogException.CatalogCannotWrite(catalogName);
        }

        return catalog;
    }

    /// <inheritdoc />
    public async Task<bool> IsExistingAsync(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var exists = await catalog.IsExistingAsync(blueprintId, sourceIdentifier).ConfigureAwait(false);
            if (exists)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<BlueprintExistingResult> IsExistingAsync(BlueprintIdVersionRange blueprintIdVersionRange,
        object? sourceIdentifier = null)
    {
        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var result = await catalog.IsExistingAsync(blueprintIdVersionRange, sourceIdentifier).ConfigureAwait(false);
            if (result.Exists)
            {
                return result;
            }
        }

        return new BlueprintExistingResult
        {
            Exists = false,
            BlueprintId = null
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlueprintCatalogRefreshResult>> RefreshAllCatalogCachesAsync(
        object? sourceIdentifier = null, bool force = false)
    {
        var results = new List<BlueprintCatalogRefreshResult>();

        foreach (var catalog in _catalogs.OrderBy(c => c.Order))
        {
            results.Add(await RefreshSingleCatalogAsync(catalog, sourceIdentifier, force).ConfigureAwait(false));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<BlueprintCatalogRefreshResult> RefreshCatalogCacheAsync(string catalogName,
        object? sourceIdentifier = null, bool force = false)
    {
        var catalog = _catalogs.FirstOrDefault(c =>
            string.Equals(c.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase));
        if (catalog == null)
        {
            throw BlueprintCatalogException.CatalogNotFound(catalogName);
        }

        return await RefreshSingleCatalogAsync(catalog, sourceIdentifier, force).ConfigureAwait(false);
    }

    private async Task<BlueprintCatalogRefreshResult> RefreshSingleCatalogAsync(IBlueprintCatalog catalog,
        object? sourceIdentifier, bool force)
    {
        // Mirror the read paths: a catalog that does not support this source or cannot be read is
        // skipped, and a single catalog that fails to refresh (disabled, unreachable, misconfigured)
        // must never abort the refresh for the other catalogs — the failure is reported instead.
        if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
        {
            return new BlueprintCatalogRefreshResult
            {
                CatalogName = catalog.CatalogName,
                Status = BlueprintCatalogRefreshStatus.Skipped,
                Message = !catalog.CanRead
                    ? "Catalog is not readable."
                    : "Catalog does not support the requested source identifier."
            };
        }

        try
        {
            await catalog.RefreshCatalogAsync(sourceIdentifier, force).ConfigureAwait(false);
            return new BlueprintCatalogRefreshResult
            {
                CatalogName = catalog.CatalogName,
                Status = BlueprintCatalogRefreshStatus.Refreshed
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh catalog {CatalogName}", catalog.CatalogName);
            return new BlueprintCatalogRefreshResult
            {
                CatalogName = catalog.CatalogName,
                Status = BlueprintCatalogRefreshStatus.Failed,
                Message = ex.Message
            };
        }
    }
}
