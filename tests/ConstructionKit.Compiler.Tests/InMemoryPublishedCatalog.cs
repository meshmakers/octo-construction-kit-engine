using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     A published (non-local) catalog held in memory, standing in for the GitHub catalogs in command tests
///     (AB#5450: local file-system entries at or above the declared version are never a baseline, so a test baseline
///     at the declared version must be "published").
/// </summary>
internal sealed class InMemoryPublishedCatalog : ICatalog
{
    public const string Name = "TestPublishedCatalog";

    private readonly Dictionary<CkModelId, CkCompiledModelRoot> _models = new();

    /// <summary>When true, every range lookup reports an unreachable source (the cache refresh failed, AB#5450).</summary>
    public bool SourceUnreachable { get; set; }

    public int Order => 50;
    public string CatalogName => Name;
    public string Description => "In-memory published catalog (tests)";
    public bool CanWrite => true;
    public bool CanRead => true;

    public Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false) => Task.CompletedTask;

    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null) => true;

    public Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        var highest = _models.Keys
            .Where(id => string.Equals(id.Name, modelIdVersionRange.Name, StringComparison.OrdinalIgnoreCase) &&
                         modelIdVersionRange.IsSatisfiedBy(id))
            .OrderByDescending(id => id.Version)
            .FirstOrDefault();
        return Task.FromResult(new ModelExistingResult
        {
            Exists = highest != null, ModelId = highest, CatalogName = highest == null ? null : Name,
            SourceUnreachable = SourceUnreachable
        });
    }

    public Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null) =>
        Task.FromResult(_models.ContainsKey(modelId));

    public Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
        _models.TryGetValue(modelId, out var model)
            ? Task.FromResult(model)
            : throw ModelCatalogException.ModelNotFound(modelId, Name);

    public Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        _models[ckCompiledModel.ModelId] = ckCompiledModel;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        await Task.CompletedTask;
        foreach (var model in _models.Values)
        {
            yield return new CatalogResultItem { CatalogName = Name, ModelId = model.ModelId, Description = model.Description };
        }
    }

    public IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier) =>
        ListAsync(sourceIdentifier);
}
