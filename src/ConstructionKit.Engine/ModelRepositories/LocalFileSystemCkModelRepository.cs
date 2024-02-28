using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
///     CkModel repository that uses the local file system to store the compiled models.
/// </summary>
public class LocalFileSystemCkModelRepository : ICkModelRepository
{
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IOptions<LocalCkModelRepositoryOptions> _options;

    /// <summary>
    ///     Creates a new instance of the <see cref="LocalFileSystemCkModelRepository" /> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="ckJsonSerializer"></param>
    public LocalFileSystemCkModelRepository(IOptions<LocalCkModelRepositoryOptions> options,
        ICkJsonSerializer ckJsonSerializer)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public string RepositoryName => "LocalRepository";

    /// <inheritdoc />
    public string Description => $"Local file system repository at '{_options.Value.RootPath}'";

    /// <inheritdoc />
    public bool CanWrite => true;

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public Task<bool> IsModelIdExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        if (!TryGetExistingModelPath(modelId, out _))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!TryGetExistingModelPath(modelId, out var compiledModelFilePath) || compiledModelFilePath == null)
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

#if NETSTANDARD2_0
        using var streamReader = File.OpenRead(compiledModelFilePath);
#else
        await using var streamReader = File.OpenRead(compiledModelFilePath);
#endif
        var compiledModelRoot = await _ckJsonSerializer
            .DeserializeCompiledModelRootAsync(streamReader, compiledModelFilePath, operationResult)
            .ConfigureAwait(false);
        if (operationResult.HasErrors)
        {
            throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName, operationResult);
        }

        return compiledModelRoot;
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var compiledModelFilePath = CreatePath(ckCompiledModel.ModelId);
        if (File.Exists(compiledModelFilePath) && !force)
        {
            throw ModelRepositoryException.ModelAlreadyExists(ckCompiledModel.ModelId, RepositoryName);
        }

        var path = Path.GetDirectoryName(compiledModelFilePath)!;
        Directory.CreateDirectory(path);

        var i = 0;
        while (i++ < 20)
        {
            try
            {
#if NETSTANDARD2_0
                using var streamWriter = new StreamWriter(compiledModelFilePath);
#else
                await using var streamWriter = new StreamWriter(compiledModelFilePath);
#endif
                await _ckJsonSerializer.SerializeAsync(streamWriter, ckCompiledModel).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        throw new NotImplementedException();
    }

    private string CreatePath(CkModelId ckModelId)
    {
        var rootPath = _options.Value.RootPath;
        var modelPath = Path.Combine(rootPath, "ck-models", ckModelId.ModelId);
        var modelVersionPath = Path.Combine(modelPath, ckModelId.ModelVersion.Major.ToString());
        var compiledModelFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.json";
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