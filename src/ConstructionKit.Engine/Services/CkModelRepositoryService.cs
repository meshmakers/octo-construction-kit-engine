using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
///     Manages the CK model repositories
/// </summary>
internal class CkModelRepositoryService : ICkModelRepositoryService
{
    private readonly ILogger<CkModelRepositoryService> _logger;
    private readonly ICkModelRepositoryManager _ckModelRepositoryManager;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkValidationService _ckValidationService;
    private readonly ICkCacheService _ckCacheService;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkModelRepositoryService" /> class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="ckModelRepositoryManager"></param>
    /// <param name="ckSerializer"></param>
    /// <param name="ckValidationService"></param>
    /// <param name="ckCacheService"></param>
    public CkModelRepositoryService(ILogger<CkModelRepositoryService> logger,
        ICkModelRepositoryManager ckModelRepositoryManager, ICkSerializer ckSerializer,
        ICkValidationService ckValidationService, ICkCacheService ckCacheService)
    {
        _logger = logger;
        _ckModelRepositoryManager = ckModelRepositoryManager;
        _ckSerializer = ckSerializer;
        _ckValidationService = ckValidationService;
        _ckCacheService = ckCacheService;
    }

    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return await _ckModelRepositoryManager
            .LookupCkModelAsync(ckModelId, operationResult, sourceIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CkCompiledModelRoot?> LookupCkModelAsync(string repositoryName, CkModelId ckModelId, OperationResult operationResult,
        CancellationToken? cancellationToken = null)
    {
        return await _ckModelRepositoryManager
            .LookupCkModelAsync(repositoryName, ckModelId, operationResult, cancellationToken)
            .ConfigureAwait(false);
    }

    public IEnumerable<Tuple<string, string>> GetRepositoryList(object? sourceIdentifier = null)
    {
        return _ckModelRepositoryManager.GetRepositoryList(sourceIdentifier);
    }

    public async Task PublishModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel, bool isForced,
        bool publishExtensions, object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        await _ckModelRepositoryManager.PublishModelAsync(repositoryName, ckCompiledModel, isForced, publishExtensions,
                sourceIdentifier,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateModelAsync(string repositoryName, CkCompiledModelRoot ckCompiledModel,
        bool publishExtensions, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        await _ckModelRepositoryManager
            .UpdateModelAsync(repositoryName, ckCompiledModel, publishExtensions, sourceIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CustomizeCkEnumAsync(string repositoryName, CkId<CkEnumId> ckEnumId,
        ICollection<CkEnumUpdate> ckEnumUpdates, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        await _ckModelRepositoryManager
            .CustomizeCkEnumAsync(repositoryName, ckEnumId, ckEnumUpdates, sourceIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsCkModelExistingAsync(string repositoryName, CkModelId ckModelId,
        object? sourceIdentifier = null)
    {
        return await _ckModelRepositoryManager.IsCkModelExistingAsync(repositoryName, ckModelId, sourceIdentifier)
            .ConfigureAwait(false);
    }


    /// <inheritdoc />
    public async Task<IEnumerable<CompileResult>> RestoreConstructionKitModelsAsync(string modelConfigurationFilePath,
        string outputPath, string? createCacheFilePath, object? sourceIdentifier = null)
    {
        var operationResult = new OperationResult();
        return await RestoreConstructionKitModelsAsync(modelConfigurationFilePath, outputPath, createCacheFilePath,
            operationResult, sourceIdentifier).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<CompileResult>> RestoreConstructionKitModelsAsync(string modelConfigurationFilePath,
        string outputPath, string? createCacheFilePath, OperationResult operationResult,
        object? sourceIdentifier = null)
    {
        ArgumentValidation.ValidateExistingFile(nameof(modelConfigurationFilePath), modelConfigurationFilePath);
        ArgumentValidation.ValidateDirectoryPath(nameof(outputPath), outputPath);
        
        modelConfigurationFilePath = MmPath.NormalizePath(modelConfigurationFilePath);
        outputPath = MmPath.NormalizePath(outputPath);
        if (!string.IsNullOrWhiteSpace(createCacheFilePath) && createCacheFilePath != null)
        {
            createCacheFilePath = MmPath.NormalizePath(createCacheFilePath);
        }

        var originFileResolver = new OriginFileResolver(modelConfigurationFilePath);

#if NETSTANDARD2_0
        using var streamReader = File.OpenRead(modelConfigurationFilePath);
#else
        await using var streamReader = File.OpenRead(modelConfigurationFilePath);
#endif
        var ckModelConfigDto = await _ckSerializer
            .DeserializeModelConfigAsync(streamReader, modelConfigurationFilePath, operationResult)
            .ConfigureAwait(false);
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }

        if (ckModelConfigDto.Imports == null)
        {
            operationResult.AddMessage(MessageCodes.NoImportsFound(modelConfigurationFilePath));
            return new List<CompileResult>();
        }

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        List<CompileResult> compileResults = new();
        foreach (var ckModelId in ckModelConfigDto.Imports)
        {
            var compiledModelFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.yaml";
            var compiledModelFilePath = Path.Combine(outputPath, compiledModelFile);

#if NETSTANDARD2_0
        if (string.IsNullOrWhiteSpace(createCacheFilePath) || createCacheFilePath == null)
#else
            if (string.IsNullOrWhiteSpace(createCacheFilePath))
#endif
            {
                compileResults.Add(new CompileResult(compiledModelFilePath));
                continue;
            }

            var compiledModelRoot = await _ckModelRepositoryManager.LookupCkModelAsync(ckModelId, operationResult)
                .ConfigureAwait(false);
            if (operationResult.HasErrors || operationResult.HasFatalErrors || compiledModelRoot == null)
            {
                _logger.LogError("Error loading model \'{ModelId}\'", ckModelId);
                throw CompilerException.OperationResultWithErrors(operationResult);
            }

#if NETSTANDARD2_0
            using var streamWriter = new StreamWriter(compiledModelFilePath);
#else
            await using var streamWriter = new StreamWriter(compiledModelFilePath);
#endif
            await _ckSerializer.SerializeAsync(streamWriter, compiledModelRoot).ConfigureAwait(false);

            var ckModelGraph = await _ckValidationService
                .ValidateAsync(compiledModelRoot, originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);

            var compiledModelCacheFilePath = await CreateCacheFileAsync(ckModelGraph, ckModelId, createCacheFilePath)
                .ConfigureAwait(false);

            compileResults.Add(new CompileResult(compiledModelFilePath, compiledModelCacheFilePath));
        }

        return compileResults;
    }

    private async Task<string> CreateCacheFileAsync(ICkModelGraph ckModelGraph, CkModelId ckModelId, string outputPath)
    {
        var tempTenantId = Guid.NewGuid().ToString();
        _ckCacheService.CreateTenant(tempTenantId);
        _ckCacheService.LoadCkModelGraph(tempTenantId, ckModelGraph);

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        var compiledModelCacheFilePath = GetCompiledModelCacheFilePath(ckModelId, outputPath);
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(compiledModelCacheFilePath);
#else
        await using var streamWriter = new StreamWriter(compiledModelCacheFilePath);
#endif
        await _ckCacheService.SaveCacheAsync(tempTenantId, streamWriter.BaseStream).ConfigureAwait(false);

        return compiledModelCacheFilePath;
    }

    private string GetCompiledModelCacheFilePath(CkModelId ckModelId, string outputPath)
    {
        var compiledModelCacheFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.cache.json";
        var compiledModelCacheFilePath = Path.Combine(outputPath, compiledModelCacheFile);
        return compiledModelCacheFilePath;
    }
}