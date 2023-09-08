using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Embedded Resource construction kit model repository
/// </summary>
public class EmbeddedResourceCkModelRepository: ICkModelRepository
{
    private readonly IEnumerable<ICkEmbeddedModel> _embeddedModels;

    /// <summary>
    /// Creates a new instance of the <see cref="EmbeddedResourceCkModelRepository"/> class.
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
    public string Description => "Embedded resource repository, construction kits that are delivered with the application.";

    /// <inheritdoc />
    public bool CanWrite => false;

    /// <inheritdoc />
    public Task<bool> LookupModelIdAsync(CkModelId modelId)
    {
        return Task.FromResult(_embeddedModels.Any(m=> m.ModelId == modelId));
    }

    /// <inheritdoc />
    public Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult)
    {
        var embeddedModel = _embeddedModels.FirstOrDefault(m => m.ModelId == modelId);
        if (embeddedModel == null)
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }
        
        return embeddedModel.GetCompiledModelRootAsync(operationResult);
    }

    /// <inheritdoc />
    public Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false)
    {
        throw ModelRepositoryException.ModelRepositoryNotWritable(RepositoryName);
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel)
    {
        throw ModelRepositoryException.ModelRepositoryNotWritable(RepositoryName);
    }
}