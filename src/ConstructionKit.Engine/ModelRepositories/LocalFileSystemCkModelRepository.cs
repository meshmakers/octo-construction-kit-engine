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
    public string RepositoryName => "LocalRepository";  

    /// <inheritdoc />
    public string Description => $"Local file system repository at '{_options.Value.RootPath}'";

    /// <inheritdoc />
    public Task<bool> LookupModelIdAsync(CkModelId modelId)
    {
        if (!TryGetExistingModelPath(modelId, out _))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId)
    {
        if (!TryGetExistingModelPath(modelId, out var compiledModelFilePath) || compiledModelFilePath == null)
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
    public async Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false)
    {
        var compiledModelFilePath = CreatePath(ckCompiledModel.ModelId);
        if (File.Exists(compiledModelFilePath) && !force)
        {
            throw ModelRepositoryException.ModelAlreadyExists(ckCompiledModel.ModelId, RepositoryName);
        }

        var path = Path.GetDirectoryName(compiledModelFilePath)!;
        Directory.CreateDirectory(path);

        await using var streamWriter = new StreamWriter(compiledModelFilePath);
        await _ckJsonSerializer.SerializeAsync(streamWriter, ckCompiledModel);
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel)
    {
        throw new NotImplementedException();
    }

    private string CreatePath(CkModelId ckModelId)
    {
        var rootPath = _options.Value.RootPath;
        var modelPath = Path.Combine(rootPath, "ck-models", ckModelId.ModelId);
        var modelVersionPath = Path.Combine(modelPath, ckModelId.ModelVersion.Major.ToString());
        string compiledModelFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.json";
        var compiledModelFilePath = Path.Combine(modelVersionPath, compiledModelFile);
        return compiledModelFilePath;
    }

    private bool TryGetExistingModelPath(CkModelId ckModelId, out string? compiledModelFilePath)
    {
        compiledModelFilePath = CreatePath(ckModelId);
        if (!File.Exists(compiledModelFilePath))
        {
            compiledModelFilePath = null;
            return false;
        }

        return true;
    }
}