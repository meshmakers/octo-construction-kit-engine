using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.ModelRepositories;

public class LocalFileSystemCkModelRepository : ICkModelRepository
{
    private readonly IOptions<LocalCkModelRepositoryOptions> _options;
    private readonly ICkJsonSerializer _ckJsonSerializer;

    public LocalFileSystemCkModelRepository(IOptions<LocalCkModelRepositoryOptions> options, ICkJsonSerializer ckJsonSerializer)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }
    
    public int Order => 0;
    public string RepositoryName => "Local Repository";

    public Task<bool> LookupModelIdAsync(CkModelId modelId)
    {
        if (!TryGetModelPath(modelId, out _))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

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

    public Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel)
    {
        throw new NotImplementedException();
    }

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