using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
///     Embedded Resource construction kit model repository
/// </summary>
public class EmbeddedResourceCkModelRepository : ICkModelRepository
{
    private readonly IEnumerable<ICkEmbeddedModel> _embeddedModels;

    /// <summary>
    ///     Creates a new instance of the <see cref="EmbeddedResourceCkModelRepository" /> class.
    /// </summary>
    public EmbeddedResourceCkModelRepository(IEnumerable<ICkEmbeddedModel> embeddedModels)
    {
        _embeddedModels = embeddedModels;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public string RepositoryName => "EmbeddedResourceRepository";

    /// <inheritdoc />
    public string Description =>
        "Embedded resource repository, construction kits that are delivered with the application.";

    /// <inheritdoc />
    public bool CanWrite => false;

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public Task<ModelExistingResult> IsModelIdExistingAsync(CkModelIdVersionRange modelIdVersionRange, object? sourceIdentifier = null)
    {
        // Find all models that satisfy the version range
        var satisfiedModels = _embeddedModels
            .Where(m => m.ModelId.ModelId == modelIdVersionRange.ModelId &&
                        modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(m.ModelId.ModelVersion))
            .ToList();

        if (!satisfiedModels.Any())
        {
            return Task.FromResult(new ModelExistingResult { Exists = false });
        }

        // Return the latest satisfied version
        var latestSatisfiedModel = satisfiedModels
            .OrderByDescending(m => m.ModelId.ModelVersion)
            .First();

        return Task.FromResult(new ModelExistingResult
        {
            Exists = true,
            ModelId = latestSatisfiedModel.ModelId
        });
    }

    /// <inheritdoc />
    public Task<bool> IsModelIdExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        return Task.FromResult(_embeddedModels.Any(m => m.ModelId == modelId));
    }

    /// <inheritdoc />
    public Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var embeddedModel = _embeddedModels.FirstOrDefault(m => m.ModelId == modelId);
        if (embeddedModel == null)
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

        return embeddedModel.GetCompiledModelRootAsync(operationResult);
    }

    /// <inheritdoc />
    public Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        bool publishExtensions = false, object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        throw ModelRepositoryException.ModelRepositoryNotWritable(RepositoryName);
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, bool publishExtensions = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        throw ModelRepositoryException.ModelRepositoryNotWritable(RepositoryName);
    }

    /// <inheritdoc />
    public Task CustomizeCkEnumAsync(CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        throw ModelRepositoryException.ModelRepositoryNotWritable(RepositoryName);
    }
}