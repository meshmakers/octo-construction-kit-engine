using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     Embedded Resource construction kit catalog
/// </summary>
public class EmbeddedResourceCatalog : ICatalog
{
    private readonly IEnumerable<ICkEmbeddedModel> _embeddedModels;

    /// <summary>
    ///     Creates a new instance of the <see cref="EmbeddedResourceCatalog" /> class.
    /// </summary>
    public EmbeddedResourceCatalog(IEnumerable<ICkEmbeddedModel> embeddedModels)
    {
        _embeddedModels = embeddedModels;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public string CatalogName => "EmbeddedResourceCatalog";

    /// <inheritdoc />
    public string Description =>
        "Embedded resource catalog, construction kits that are delivered with the application.";

    /// <inheritdoc />
    public bool CanWrite => false;

    /// <inheritdoc />
    public bool CanRead => true;

    /// <inheritdoc />
    public Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        // Embedded resources are static, nothing to refresh
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange, object? sourceIdentifier = null)
    {
        // Find all models that satisfy the version range
        var satisfiedModels = _embeddedModels
            .Where(m => m.ModelId.Name == modelIdVersionRange.Name &&
                        modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(m.ModelId.Version))
            .ToList();

        if (!satisfiedModels.Any())
        {
            return Task.FromResult(new ModelExistingResult { Exists = false, CatalogName = CatalogName });
        }

        // Return the latest satisfied version
        var latestSatisfiedModel = satisfiedModels
            .OrderByDescending(m => m.ModelId.Version)
            .First();

        return Task.FromResult(new ModelExistingResult
        {
            Exists = true,
            ModelId = latestSatisfiedModel.ModelId,
            CatalogName = CatalogName
        });
    }

    /// <inheritdoc />
    public Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        return Task.FromResult(_embeddedModels.Any(m => m.ModelId == modelId));
    }

    /// <inheritdoc />
    public Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var embeddedModel = _embeddedModels.FirstOrDefault(m => m.ModelId == modelId);
        if (embeddedModel == null)
        {
            throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
        }

        return embeddedModel.GetCompiledModelRootAsync(operationResult);
    }

    /// <inheritdoc />
    public Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        throw ModelCatalogException.CatalogNotWritable(CatalogName);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        await Task.Yield();

        foreach (var ckEmbeddedModel in _embeddedModels)
        {
            yield return new CatalogResultItem
            {
                ModelId = ckEmbeddedModel.ModelId,
                Description = ckEmbeddedModel.Description,
                CatalogName = CatalogName
            };
        }

    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier)
    {
        await Task.Yield();

        var searchTermTrimmed = searchTerm.ToLower().Trim();

        foreach (var ckEmbeddedModel in _embeddedModels
                     .Where(m => m.ModelId.Name.ToLower().Contains(searchTermTrimmed) ||
                                 m.Description.ToLower().Contains(searchTermTrimmed)))
        {
            yield return new CatalogResultItem
            {
                ModelId = ckEmbeddedModel.ModelId,
                Description = ckEmbeddedModel.Description,
                CatalogName = CatalogName
            };
        }
    }
}