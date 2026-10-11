using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
///     Manages the CK model repositories
/// </summary>
internal class CatalogService : ICatalogService
{
    private readonly ILogger<CatalogService> _logger;
    private readonly ICatalogManager _catalogManager;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICatalogModelResolver _catalogModelResolver;
    private readonly ICkCacheService _ckCacheService;
    private readonly Lazy<CkPublishGate> _publishGate;

    /// <summary>
    ///     Creates a new instance of the <see cref="CatalogService" /> class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="catalogManager"></param>
    /// <param name="ckSerializer"></param>
    /// <param name="catalogModelResolver"></param>
    /// <param name="ckCacheService"></param>
    /// <param name="publishGate">
    ///     The compatibility gate every publish passes (AB#6386). Lazy because the gate itself reads through
    ///     <see cref="ICatalogService" />.
    /// </param>
    public CatalogService(ILogger<CatalogService> logger,
        ICatalogManager catalogManager, ICkSerializer ckSerializer,
        ICatalogModelResolver catalogModelResolver, ICkCacheService ckCacheService,
        Lazy<CkPublishGate> publishGate)
    {
        _logger = logger;
        _catalogManager = catalogManager;
        _ckSerializer = ckSerializer;
        _catalogModelResolver = catalogModelResolver;
        _ckCacheService = ckCacheService;
        _publishGate = publishGate;
    }

    public Task<ModelSearchResult> SearchAsync(string searchTerm, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return _catalogManager.SearchAsync(searchTerm, skip, take, sourceIdentifier,
            cancellationToken);
    }

    public Task<ModelSearchResult> SearchAsync(string catalogName, string searchTerm, int skip, int take,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return _catalogManager.SearchAsync(catalogName, searchTerm, skip, take, sourceIdentifier,
            cancellationToken);
    }

    public Task<ModelListResult> ListAsync(int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return _catalogManager.ListAsync(skip, take, sourceIdentifier,
            cancellationToken);
    }

    public Task<ModelListResult> ListAsync(string catalogName, int skip, int take, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return _catalogManager.ListAsync(catalogName, skip, take, sourceIdentifier,
            cancellationToken);
    }

    public Task<IReadOnlyList<CatalogResultItem>> ListVersionsAsync(string ckModelName,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        return _catalogManager.ListVersionsAsync(ckModelName, sourceIdentifier, cancellationToken);
    }

    public async Task<CkCompiledModelRoot?> GetAsync(CkModelId ckModelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        return await _catalogManager
            .GetAsync(ckModelId, operationResult, sourceIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CkCompiledModelRoot?> GetAsync(string catalogName, CkModelId ckModelId,
        OperationResult operationResult,
        CancellationToken? cancellationToken = null)
    {
        return await _catalogManager
            .GetAsync(catalogName, ckModelId, operationResult, cancellationToken)
            .ConfigureAwait(false);
    }

    public IEnumerable<Tuple<string, string>> GetCatalogList(object? sourceIdentifier = null)
    {
        return _catalogManager.GetCatalogList(sourceIdentifier);
    }

    /// <inheritdoc />
    public Task PublishAsync(string catalogName, CkCompiledModelRoot ckCompiledModel,
        OriginFileResolver originFileResolver, bool isForced,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        return PublishAsync(catalogName, ckCompiledModel, originFileResolver, isForced, new OperationResult(),
            sourceIdentifier, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishAsync(string catalogName, CkCompiledModelRoot ckCompiledModel,
        OriginFileResolver originFileResolver, bool isForced, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        // Only the messages produced by THIS resolve decide whether the publish may proceed: the caller
        // may hand in a result that already carries messages from an earlier step, and attributing those
        // to the publish would both misreport the cause and change which step is blamed.
        var messageCountBeforeResolve = operationResult.Messages.Count;
        await _catalogModelResolver.HardResolveAsync(ckCompiledModel, originFileResolver, operationResult)
            .ConfigureAwait(false);
        var resolveMessages = operationResult.Messages.Skip(messageCountBeforeResolve).ToList();

        // AB#5453: a resolve that collects errors instead of throwing (unknown type/record/attribute
        // reference, duplicate id, broken display rule, circular dependency, ...) used to return here
        // quietly. The caller then logged "published" for a model that never reached the catalog and
        // exited 0, which let the catalogs release train record a model as published that is not there.
        // The dependency half of the same resolve has always thrown (ModelValidationException.UnknownCkModels);
        // this makes the remaining half just as loud.
        if (resolveMessages.Exists(x =>
                x.MessageLevel is MessageLevel.Error or MessageLevel.FatalError))
        {
            throw CompilerException.PublishFailedWithErrors(catalogName, ckCompiledModel.ModelId, resolveMessages,
                operationResult);
        }

        // AB#6386: gate 2 of the compatibility concept. Every publish to a writable catalog except the local
        // file-system catalog gets the compile gate's verdict against the target catalog; there is no bypass switch
        // (isForced only decides whether an existing file may be overwritten, never whether the gate runs).
        var gateResult = await _publishGate.Value.EvaluateAsync(catalogName, ckCompiledModel, sourceIdentifier)
            .ConfigureAwait(false);
        foreach (var message in gateResult.Messages)
        {
            AddGateMessage(operationResult, ckCompiledModel, message);
        }

        switch (gateResult.Decision)
        {
            case CkPublishGateDecision.Refused:
                throw CompilerException.PublishRefusedByGate(catalogName, ckCompiledModel.ModelId,
                    gateResult.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => (m.Code, m.Text)),
                    operationResult);
            case CkPublishGateDecision.AlreadyPublished:
                _logger.LogInformation("Model {ModelId} is already published to catalog {CatalogName}, nothing to do",
                    ckCompiledModel.ModelId, catalogName);
                return;
        }

        await _catalogManager
            .PublishAsync(catalogName, ckCompiledModel, isForced, sourceIdentifier, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UnpublishPlan> PlanUnpublishAsync(string catalogName, CkModelId modelId,
        CancellationToken? cancellationToken = null)
    {
        await _catalogManager.RefreshCatalogCacheAsync(catalogName, null, true).ConfigureAwait(false);
        var models = await ListCatalogModelsAsync(catalogName, cancellationToken).ConfigureAwait(false);
        var isPublished = models.Contains(modelId);
        if (!isPublished)
        {
            return new UnpublishPlan { ModelId = modelId, CatalogName = catalogName, IsPublished = false };
        }

        var dependents = await FindUnpublishDependentsAsync(catalogName, modelId, models, cancellationToken)
            .ConfigureAwait(false);
        return new UnpublishPlan
        {
            ModelId = modelId, CatalogName = catalogName, IsPublished = true, Dependents = dependents
        };
    }

    /// <inheritdoc />
    public async Task<CatalogUnpublishResult> UnpublishAsync(string catalogName, CkModelId modelId, string? reason,
        CancellationToken? cancellationToken = null)
    {
        if (string.Equals(catalogName, PublicGitHubCatalog.Name, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(reason))
        {
            throw ModelCatalogException.UnpublishReasonRequired(catalogName);
        }

        var plan = await PlanUnpublishAsync(catalogName, modelId, cancellationToken).ConfigureAwait(false);
        if (plan.Dependents.Count > 0)
        {
            throw ModelCatalogException.UnpublishRefusedDependents(modelId, catalogName, plan.Dependents);
        }

        _logger.LogInformation("Unpublishing {ModelId} from catalog {CatalogName}", modelId, catalogName);
        return await _catalogManager.UnpublishAsync(catalogName, modelId, reason, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HashSet<CkModelId>> ListCatalogModelsAsync(string catalogName,
        CancellationToken? cancellationToken)
    {
        var result = await _catalogManager.ListAsync(catalogName, 0, int.MaxValue, null, cancellationToken)
            .ConfigureAwait(false);
        return new HashSet<CkModelId>(result.ModelResultItems.Select(i => i.ModelId));
    }

    /// <summary>
    ///     The published models of the catalog that would be left without a satisfying version: a dependency range
    ///     (the exact pin of a classic model, the declared range of a range-retaining one) that the unpublished version
    ///     satisfies and no remaining version of the same model satisfies.
    /// </summary>
    private async Task<IReadOnlyList<UnpublishDependent>> FindUnpublishDependentsAsync(string catalogName,
        CkModelId target, HashSet<CkModelId> models, CancellationToken? cancellationToken)
    {
        var remaining = models.Where(m => string.Equals(m.Name, target.Name, StringComparison.OrdinalIgnoreCase) &&
                                          m != target).ToList();
        var candidates = models.Where(m => !string.Equals(m.Name, target.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Version).ToList();

        var dependents = new System.Collections.Concurrent.ConcurrentBag<UnpublishDependent>();
        using var gate = new SemaphoreSlim(8);
        var tasks = candidates.Select(async candidate =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                cancellationToken?.ThrowIfCancellationRequested();
                var operationResult = new OperationResult();
                var compiled = await _catalogManager
                    .GetAsync(catalogName, candidate, operationResult, cancellationToken).ConfigureAwait(false);
                foreach (var range in compiled.GetResolutionRanges())
                {
                    if (string.Equals(range.Name, target.Name, StringComparison.OrdinalIgnoreCase) &&
                        range.IsSatisfiedBy(target) && !remaining.Any(range.IsSatisfiedBy))
                    {
                        dependents.Add(new UnpublishDependent(candidate, range.ToString()));
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        return dependents.OrderBy(d => d.ModelId.Name, StringComparer.Ordinal).ThenBy(d => d.ModelId.Version)
            .ToList();
    }

    private void AddGateMessage(OperationResult operationResult, CkCompiledModelRoot model,
        CkCompileGateMessage message)
    {
        if (message.Severity == CkCompileGateSeverity.Error)
        {
            // "OCTO-CK200" -> message number 200; the code stays in the text so the log reader sees it.
            var number = message.Code != null && int.TryParse(message.Code.Substring(message.Code.Length - 3), out var n)
                ? n
                : 0;
            operationResult.AddMessage(new OperationMessage(MessageLevel.Error, model.ModelId.FullName, number,
                $"{message.Code}: {message.Text}"));
        }
        else if (message.Severity == CkCompileGateSeverity.Warning)
        {
            // Logger only, not the OperationResult: a warning there fails every build that treats warnings as errors,
            // and the no-op it announces is a success.
            _logger.LogWarning("{Message}", message.Text);
        }
        else
        {
            _logger.LogInformation("{Message}", message.Text);
        }
    }

    public async Task<ModelExistingResult> IsExistingAsync(string catalogName,
        CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        return await _catalogManager.IsExistingAsync(catalogName, ckModelIdVersionRange, sourceIdentifier)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsExistingAsync(CkModelId ckModelId, object? sourceIdentifier = null)
    {
        return await _catalogManager.IsExistingAsync(ckModelId, sourceIdentifier)
            .ConfigureAwait(false);
    }

    public async Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange ckModelIdVersionRange,
        object? sourceIdentifier = null)
    {
        return await _catalogManager.IsExistingAsync(ckModelIdVersionRange, sourceIdentifier)
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

        List<CompileResult> compileResults = [];
        foreach (var ckModelIdVersionRange in ckModelConfigDto.Imports)
        {
            var ckModelExistingResult = await _catalogManager
                .IsExistingAsync(ckModelIdVersionRange, sourceIdentifier).ConfigureAwait(false);

            if (!ckModelExistingResult.Exists || ckModelExistingResult.ModelId == null)
            {
                operationResult.AddMessage(
                    MessageCodes.UnknownCkModel(originFileResolver.Resolve(ckModelIdVersionRange),
                        ckModelIdVersionRange));
                throw ModelValidationException.UnknownCkModel(ckModelIdVersionRange);
            }

            var compiledModelFile = $"ck-{ckModelExistingResult.ModelId.SemanticVersionedFullName.ToLower()}.yaml";
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


            var compiledModelRoot = await _catalogManager.GetAsync(ckModelExistingResult.ModelId, operationResult)
                .ConfigureAwait(false);
            if (operationResult.HasErrors || operationResult.HasFatalErrors || compiledModelRoot == null)
            {
                _logger.LogError("Error loading model \'{Name}\'", ckModelExistingResult.ModelId);
                throw CompilerException.OperationResultWithErrors(operationResult);
            }

#if NETSTANDARD2_0
            using var streamWriter = new StreamWriter(compiledModelFilePath);
#else
            await using var streamWriter = new StreamWriter(compiledModelFilePath);
#endif
            await _ckSerializer.SerializeAsync(streamWriter, compiledModelRoot).ConfigureAwait(false);

            var ckModelGraph = await _catalogModelResolver
                .HardResolveAsync(compiledModelRoot, originFileResolver, operationResult, sourceIdentifier)
                .ConfigureAwait(false);

            var compiledModelCacheFilePath =
                await CreateCacheFileAsync(ckModelGraph, ckModelExistingResult.ModelId, createCacheFilePath)
                    .ConfigureAwait(false);

            compileResults.Add(new CompileResult(compiledModelFilePath, compiledModelCacheFilePath));
        }

        return compileResults;
    }

    public Task RefreshCatalogCacheAsync(string catalogName, object? sourceIdentifier = null,
        bool forceRefresh = false)
    {
        return _catalogManager.RefreshCatalogCacheAsync(catalogName, sourceIdentifier, forceRefresh);
    }

    public Task RefreshAllCatalogCachesAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        return _catalogManager.RefreshAllCatalogCachesAsync(sourceIdentifier, forceRefresh);
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