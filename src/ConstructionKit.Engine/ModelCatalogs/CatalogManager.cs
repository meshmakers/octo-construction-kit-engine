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

        Dictionary<CkModelId, CatalogResultItem> allModelResultItems = new();

        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            _logger.LogInformation("Checking catalog {CatalogName} for models",
                catalog.CatalogName);

            await foreach (var modelResultItem in catalog.SearchAsync(searchTerm, sourceIdentifier).ConfigureAwait(false))
            {
                if (!allModelResultItems.ContainsKey(modelResultItem.ModelId))
                {
                    allModelResultItems[modelResultItem.ModelId] = modelResultItem;
                }
            }
        }

        return new ModelSearchResult
        {
            SearchTerm = searchTerm,
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allModelResultItems.Count,
            ModelResultItems = allModelResultItems.Values.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelSearchResult> SearchAsync(string catalogName, string searchTerm, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Searching CK models in catalog {CatalogName} with term {SearchTerm}", catalogName, searchTerm);

        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        int count = 0;
        int taken = 0;
        List<CatalogResultItem> modelResultItems = new List<CatalogResultItem>();
        await foreach (var modelResultItem in catalog.SearchAsync(searchTerm, sourceIdentifier).ConfigureAwait(false))
        {
            if (count++ < skip)
            {
                continue;
            }

            if (taken++ >= take)
            {
                break;
            }

            modelResultItems.Add(modelResultItem);
        }

        return new ModelSearchResult
        {
            SearchTerm = searchTerm,
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = modelResultItems.Count,
            ModelResultItems = modelResultItems.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelListResult> ListAsync(int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Listing CK models in catalogs");

        Dictionary<CkModelId, CatalogResultItem> allModelResultItems = new();

        int count = 0;
        int taken = 0;
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            _logger.LogInformation("Checking catalog {CatalogName} for models",
                catalog.CatalogName);

            List<CatalogResultItem> modelResultItems = new List<CatalogResultItem>();
            await foreach (var modelResultItem in catalog.ListAsync(sourceIdentifier).ConfigureAwait(false))
            {
                if (count++ < skip)
                {
                    continue;
                }

                if (taken++ >= take)
                {
                    break;
                }

                modelResultItems.Add(modelResultItem);
            }

            foreach (var modelResultItem in modelResultItems)
            {
                if (!allModelResultItems.ContainsKey(modelResultItem.ModelId))
                {
                    allModelResultItems[modelResultItem.ModelId] = modelResultItem;
                }
            }
        }

        return new ModelListResult
        {
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = allModelResultItems.Count,
            ModelResultItems = allModelResultItems.Values.Skip(skip).Take(take).ToList()
        };
    }

    public async Task<ModelListResult> ListAsync(string catalogName, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _logger.LogInformation("Listing CK models in catalog {CatalogName}", catalogName);

        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        int count = 0;
        int taken = 0;
        List<CatalogResultItem> modelResultItems = new List<CatalogResultItem>();
        await foreach (var modelResultItem in catalog.ListAsync(sourceIdentifier).ConfigureAwait(false))
        {
            if (count++ < skip)
            {
                continue;
            }

            if (taken++ >= take)
            {
                break;
            }

            modelResultItems.Add(modelResultItem);
        }

        return new ModelListResult
        {
            SkippedCount = skip,
            TakeCount = take,
            TotalCount = modelResultItems.Count,
            ModelResultItems = modelResultItems.Skip(skip).Take(take).ToList()
        };
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
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (!catalog.IsSupportingSourceIdentifier(sourceIdentifier) || !catalog.CanRead)
            {
                continue;
            }

            var modelExistingResult = await catalog.IsExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);
            if (modelExistingResult.Exists)
            {
                return modelExistingResult;
            }
        }

        return new ModelExistingResult { Exists = false  };
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

    public async Task RefreshCatalogCacheAsync(string catalogName)
    {
        var catalog = _catalogs.FirstOrDefault(x => string.Compare(x.CatalogName,
            catalogName, StringComparison.OrdinalIgnoreCase) == 0);
        if (catalog == null)
        {
            throw ModelCatalogException.ModelCatalogNotFound(catalogName);
        }

        await catalog.RefreshCatalogAsync().ConfigureAwait(false);
    }
}