using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// CkModel repository that uses the local file system to store the compiled models.
/// </summary>
public class LocalFileSystemCkModelRepository : ICkModelRepository
{
    private readonly IOptions<LocalCkModelRepositoryOptions> _options;
    private readonly ICkJsonSerializer _ckJsonSerializer;

    /// <summary>
    /// Creates a new instance of the <see cref="LocalFileSystemCkModelRepository"/> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="ckJsonSerializer"></param>
    public LocalFileSystemCkModelRepository(IOptions<LocalCkModelRepositoryOptions> options, ICkJsonSerializer ckJsonSerializer)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <inheritdoc />
    public int Order => 0;
    /// <inheritdoc />
    public string RepositoryName => "Local Repository";

    /// <inheritdoc />
    public Task<bool> LookupModelIdAsync(CkModelId modelId)
    {
        if (!TryGetModelPath(modelId, out _))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId)
    {
        if (!TryGetModelPath(modelId, out var compiledModelFilePath) || compiledModelFilePath == null)
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

        OperationResult operationResult = new();
        await using var streamReader = File.OpenRead(compiledModelFilePath);
        var compiledModelRoot = await _ckJsonSerializer.DeserializeCompiledModelRootAsync(streamReader, operationResult);
        if (operationResult.HasErrors)
        {
            throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName, operationResult);
        }

        return compiledModelRoot;
    }

    /// <inheritdoc />
    public Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel)
    {
        throw new NotImplementedException();
    }

    private bool TryGetModelPath(CkModelId ckModelId, out string? compiledModelFilePath)
    {
        var rootPath = _options.Value.RootPath;
        var modelPath = Path.Combine(rootPath, "ck-models", ckModelId.ModelId);
        if (!Directory.Exists(modelPath))
        {
            compiledModelFilePath = null;
            return false;
        }
        
        var modelVersionPath = Path.Combine(modelPath, ckModelId.ModelVersion.Major.ToString());
        if (!Directory.Exists(modelVersionPath))
        {
            compiledModelFilePath = null;
            return false;
        }
        
        string compiledModelFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.yaml";
        compiledModelFilePath = Path.Combine(modelPath, compiledModelFile);
        if (!File.Exists(compiledModelFilePath))
        {
            compiledModelFilePath = null;
            return false;
        }

        return true;
    }
}