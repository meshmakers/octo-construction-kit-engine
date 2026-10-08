using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     Manages the catalogs that can be used to look up a compiled model.
/// </summary>
internal class CatalogManager : ICatalogManager
{
    private readonly ILogger<CatalogManager> _logger;
    private readonly IEnumerable<ICatalog> _catalogs;

    /// <summary>
    ///     Creates a new instance of the <see cref="CatalogManager" /> class.
    /// </summary>
    /// <param name="logger">Logger for this class.</param>
    /// <param name="catalogs">List of construction kit model catalogs.</param>
    public CatalogManager(ILogger<CatalogManager> logger,
        IEnumerable<ICatalog> catalogs)
    {
        _logger = logger;
        _catalogs = catalogs;
    }

    public async Task<ModelSearchResult> SearchAsync(string searchTerm, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Searching CK models in catalogs with term {SearchTerm}", searchTerm);

        var allItems = await CollectMergedAsync(catalog => catalog.SearchAsync(searchTerm, sourceIdentifier),
            sourceIdentifier, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new ModelSearchResult
        {
            SearchTerm = searchTerm,
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allItems.Count,
            ModelResultItems = allItems.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelSearchResult> SearchAsync(string catalogName, string searchTerm, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Searching CK models in catalog {CatalogName} with term {SearchTerm}", catalogName, searchTerm);

        var catalog = GetCatalogOrThrow(catalogName);

        var allItems = await CollectAsync(catalog.SearchAsync(searchTerm, sourceIdentifier),
            cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new ModelSearchResult
        {
            SearchTerm = searchTerm,
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allItems.Count,
            ModelResultItems = allItems.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelListResult> ListAsync(int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Listing CK models in catalogs");

        var allItems = await CollectMergedAsync(catalog => catalog.ListAsync(sourceIdentifier),
            sourceIdentifier, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new ModelListResult
        {
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allItems.Count,
            ModelResultItems = allItems.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelListResult> ListAsync(string catalogName, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Listing CK models in catalog {CatalogName}", catalogName);

        var catalog = GetCatalogOrThrow(catalogName);

        var allItems = await CollectAsync(catalog.ListAsync(sourceIdentifier),
            cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return new ModelListResult
        {
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allItems.Count,
            ModelResultItems = allItems.Skip(skip).Take(take).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogResultItem>> ListVersionsAsync(string ckModelName,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (string.IsNullOrWhiteSpace(ckModelName))
        {
            throw new ArgumentException("CK model name must not be empty.", nameof(ckModelName));
        }

        _logger.LogDebug("Listing versions of CK model {CkModelName} in catalogs", ckModelName);

        var allItems = await CollectMergedAsync(catalog => catalog.ListAsync(sourceIdentifier),
            sourceIdentifier, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

        return allItems
            .Where(item => string.Equals(item.ModelId.Name, ckModelName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.ModelId.Version)
            .ToList();
    }

    private ICatalog GetCatalogOrThrow(string catalogName)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        return catalog;
    }

    /// <summary>
    /// Merges the entries of all readable catalogs into one list, in catalog order. A model id present in
    /// several catalogs is taken from the catalog with the lowest <c>Order</c>. Skip/take is applied by the
    /// callers on the complete, deduplicated list (AB#5650: the old loop counted skip/take per raw entry
    /// across catalogs, counted duplicates against the window and then applied skip a second time).
    /// </summary>
    private async Task<List<CatalogResultItem>> CollectMergedAsync(
        Func<ICatalog, IAsyncEnumerable<CatalogResultItem>> enumerate,
        object? sourceIdentifier, CancellationToken cancellationToken)
    {
        var allItems = new List<CatalogResultItem>();
        var seenIds = new HashSet<CkModelId>();

        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            _logger.LogInformation("Checking catalog {CatalogName} for models", catalog.CatalogName);

            await foreach (var item in enumerate(catalog).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (seenIds.Add(item.ModelId))
                {
                    allItems.Add(item);
                }
            }
        }

        return allItems;
    }

    private static async Task<List<CatalogResultItem>> CollectAsync(IAsyncEnumerable<CatalogResultItem> items,
        CancellationToken cancellationToken)
    {
        var result = new List<CatalogResultItem>();
        await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result.Add(item);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot?> TryGetAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in catalogs", ckModelId);

        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            _logger.LogInformation("Checking catalog {CatalogName} for model {CkModelId}",
                catalog.CatalogName, ckModelId);

            var hasBeenFound = await catalog.IsExistingAsync(ckModelId, sourceIdentifier)
                .ConfigureAwait(false);
            if (hasBeenFound)
            {
                _logger.LogInformation("Found model {CkModelId} in catalog {CatalogName}", ckModelId, catalog.CatalogName);
                return await catalog.GetAsync(ckModelId, operationResult, sourceIdentifier)
                    .ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot?> TryGetAsync(string catalogName, CkModelId ckModelId,
        OperationResult operationResult,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in catalog {CatalogName}", ckModelId,
            catalogName);

        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        _logger.LogInformation("Checking catalog {CatalogName} for model {CkModelId}",
            catalog.CatalogName, ckModelId);

        var hasBeenFound = await catalog.IsExistingAsync(ckModelId)
            .ConfigureAwait(false);
        if (hasBeenFound)
        {
            return await catalog.GetAsync(ckModelId, operationResult)
                .ConfigureAwait(false);
        }

        return null;
    }

        /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in catalogs", ckModelId);

        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            _logger.LogInformation("Checking catalog {CatalogName} for model {CkModelId}",
                catalog.CatalogName, ckModelId);

            var hasBeenFound = await catalog.IsExistingAsync(ckModelId, sourceIdentifier)
                .ConfigureAwait(false);
            if (hasBeenFound)
            {
                _logger.LogInformation("Found model {CkModelId} in catalog {CatalogName}", ckModelId, catalog.CatalogName);
                return await catalog.GetAsync(ckModelId, operationResult, sourceIdentifier)
                    .ConfigureAwait(false);
            }
        }

        throw ModelCatalogException.ModelNotFoundInCatalogs(ckModelId);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetAsync(string catalogName, CkModelId ckModelId,
        OperationResult operationResult,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Looking up CK model with id {CkModelId} in catalog {CatalogName}", ckModelId,
            catalogName);

        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        _logger.LogInformation("Checking catalog {CatalogName} for model {CkModelId}",
            catalog.CatalogName, ckModelId);

        var hasBeenFound = await catalog.IsExistingAsync(ckModelId)
            .ConfigureAwait(false);
        if (hasBeenFound)
        {
            return await catalog.GetAsync(ckModelId, operationResult)
                .ConfigureAwait(false);
        }

        throw ModelCatalogException.ModelNotFoundInCatalogs(ckModelId);
    }

    /// <inheritdoc />
    public IEnumerable<Tuple<string, string>> GetCatalogList(object? sourceIdentifier = null)
    {
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            yield return new Tuple<string, string>(catalog.CatalogName, catalog.Description);
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(string catalogName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier))
        {
            throw ModelCatalogException.CatalogDoesNotSupportSourceIdentifier(catalogName);
        }

        if (!catalog.CanWrite)
        {
            throw ModelCatalogException.CatalogNotWritable(catalogName);
        }

        await catalog.PublishAsync(ckCompiledModel, isForced, sourceIdentifier)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsExistingAsync(string catalogName, CkModelId ckModelId, object? sourceIdentifier = null)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        return await catalog.IsExistingAsync(ckModelId, sourceIdentifier).ConfigureAwait(false);
    }

    public async Task<bool> IsExistingAsync(CkModelId ckModelId, object? sourceIdentifier = null)
    {
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var isExisting = await catalog.IsExistingAsync(ckModelId, sourceIdentifier).ConfigureAwait(false);
            if (isExisting)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        List<ModelExistingResult> results = new();
        var anySourceUnreachable = false;
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var modelExistingResult = await catalog.IsExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);
            anySourceUnreachable |= modelExistingResult.SourceUnreachable;
            if (modelExistingResult.Exists)
            {
                results.Add(modelExistingResult);
            }
        }

        if (results.Count > 0)
        {
            // Return the highest version found; propagate whether any queried catalog source
            // was unreachable so callers can flag a potentially stale baseline.
            var highest = results
                .Where(x => x.ModelId != null)
                .OrderByDescending(x => x.ModelId)
                .First();
            return highest with { SourceUnreachable = anySourceUnreachable };
        }

        return new ModelExistingResult { Exists = false, SourceUnreachable = anySourceUnreachable };
    }

    public async Task<ModelExistingResult> IsExistingAsync(string catalogName, CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        return await catalog.IsExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);
    }

    public async Task RefreshCatalogCacheAsync(string catalogName, object? sourceIdentifier = null,
        bool forceRefresh = false)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        await catalog.RefreshCatalogAsync(null, forceRefresh).ConfigureAwait(false);
    }

    public async Task RefreshAllCatalogCachesAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            await catalog.RefreshCatalogAsync(sourceIdentifier, forceRefresh).ConfigureAwait(false);
        }
    }
}