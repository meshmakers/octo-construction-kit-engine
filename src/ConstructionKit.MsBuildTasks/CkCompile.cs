using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
/// Compiles a construction kit using msbuild
/// </summary>
public class CkCompile : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// A list of folders containing construction kits
    /// </summary>
    [Required]
    public ITaskItem[] ConstructionKitFolders { get; set; } = null!;

    /// <summary>
    /// When true, the construction kit folder is compiled
    /// </summary>
    [Required]
    public bool Compile { get; set; } = false;

    /// <summary>
    /// When a directory defined, a cache files are created containing all dependencies
    /// </summary>
    public string? CacheFilePath { get; set; }

    /// <summary>
    /// When true, the local catalog is enabled.
    /// </summary>
    public bool IsLocalCatalogEnabled { get; set; } = true;

    /// <summary>
    /// Root path of the local file system catalog. When empty, the catalog stays at its
    /// default location (~/.octo/local-catalog).
    /// </summary>
    public string? LocalCatalogRootPath { get; set; }

    /// <summary>
    /// When true, the public GitHub catalog is consulted during dependency resolution. Set to false to ignore stale cache entries from public GitHub when working purely with locally built models.
    /// </summary>
    public bool IsPublicGitHubCatalogEnabled { get; set; } = true;

    /// <summary>
    /// When true, the private GitHub catalog is consulted during dependency resolution. Set to false to ignore stale cache entries from private GitHub when working purely with locally built models.
    /// </summary>
    public bool IsPrivateGitHubCatalogEnabled { get; set; } = true;

    /// <summary>
    /// When true, the compiled construction kit model is published to the local catalog
    /// </summary>
    [Required]
    public bool PublishCkModel { get; set; } = true;

    /// <summary>
    /// CK v2 range retention (AB#5664, spike, default off): MSBuild property OctoCkRangeRetention.
    /// Empty keeps the compiler default (environment variable OctoCkRangeRetention).
    /// </summary>
    public string? RangeRetention { get; set; }

    /// <summary>
    /// AB#6294: where the compile gate takes the compatibility baseline from (MSBuild property
    /// OctoCkCompatibilityBaseline): "Local" (local file-system catalog plus cached remote catalogs, needs no
    /// network) or "Remote" (GitHub catalogs, local catalog ignored). Empty selects the default: Remote on a CI
    /// build (TF_BUILD / ContinuousIntegrationBuild), otherwise Local.
    /// </summary>
    public string? CompatibilityBaseline { get; set; }

    /// <summary>
    /// When true, the compiled construction kit model is generated as .md files
    /// </summary>
    [Required]
    public bool GenerateCkDocumentation { get; set; } = true;

    /// <summary>
    /// Defines the name of the catalog the model is published.
    /// </summary>
    [Required]
    public string PublishCatalogName { get; set; } = LocalFileSystemCatalog.Name;

    /// <summary>
    /// When true, refresh the registered remote catalogs (Public/Private GitHub) at the start
    /// of the compile step. Set to false on feature/dev branches that only consume LocalFs deps
    /// from sibling projects in the same solution build — skipping the refresh cuts the GitHub
    /// REST API quota that the catalog-walk consumes.
    /// </summary>
    public bool RefreshRemoteCatalogs { get; set; } = true;

    /// <summary>
    /// Defines the Public GitHub API Key for accessing public GitHub catalogs
    /// </summary>
    public string? PublicGitHubApiKey { get; set; }

    /// <summary>
    /// Defines the Private GitHub API Key for accessing private GitHub catalogs
    /// </summary>
    public string? PrivateGitHubApiKey { get; set; }

    /// <summary>
    /// Repository owner of the PRIVATE GitHub catalog. Empty keeps the compiled-in default.
    /// </summary>
    /// <remarks>
    /// AB#5412: the private catalog slot is lane-scoped, see
    /// <see cref="PrivateGitHubCatalogCoordinates" />. Set as a group from the pipeline
    /// (OctoPrivateGitHubCatalogOwner / ...RepositoryName / ...Branch / ...PagesUri); a partial set is
    /// accepted and merges with the defaults, which is only ever useful for a branch override.
    /// </remarks>
    public string? PrivateGitHubCatalogOwner { get; set; }

    /// <summary>
    /// Repository name of the PRIVATE GitHub catalog. Empty keeps the compiled-in default.
    /// </summary>
    public string? PrivateGitHubCatalogRepositoryName { get; set; }

    /// <summary>
    /// Repository branch of the PRIVATE GitHub catalog. Empty keeps the compiled-in default.
    /// </summary>
    public string? PrivateGitHubCatalogBranch { get; set; }

    /// <summary>
    /// GitHub Pages URI the PRIVATE GitHub catalog is READ from. Empty keeps the compiled-in default.
    /// Publishes always go through the GitHub API, so a wrong value here fails reads, not writes.
    /// </summary>
    public string? PrivateGitHubCatalogPagesUri { get; set; }

    /// <summary>
    /// Gets or sets the output path
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Link path. Should Be the Root Directory of the Documentation
    /// </summary>
    [Required]
    public string CkLinkPath { get; set; } = null!;

    /// <summary>
    /// A list of compiled models that has been generated
    /// </summary>
    [Output]
    public string[] CompiledModelFiles { get; set; } = null!;

    /// <summary>
    /// A list of cache files that has been generated
    /// </summary>
    [Output]
    public string[] CacheFiles { get; set; } = null!;

    public override bool Execute()
    {
        // AB#5412: fail before anything touches a catalog — an unexpanded macro must never be
        // mistaken for a repository name.
        if (!PrivateGitHubCatalogCoordinates.Validate(PrivateGitHubCatalogOwner,
                PrivateGitHubCatalogRepositoryName, PrivateGitHubCatalogBranch, PrivateGitHubCatalogPagesUri,
                out var coordinateError))
        {
            Log.LogError(coordinateError);
            return false;
        }

        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddConstructionKit();
        services.AddDocumentationService();

        if (!string.IsNullOrWhiteSpace(RangeRetention))
        {
            var rangeRetention = string.Equals(RangeRetention, "true", StringComparison.OrdinalIgnoreCase);
            services.Configure<CkCompilerOptions>(options => options.RangeRetention = rangeRetention);
        }

        services.Configure<LocalFileSystemCatalogOptions>(options =>
        {
            options.IsEnabled = IsLocalCatalogEnabled;
            options.ApplyRootPath(LocalCatalogRootPath);
        });

        services.Configure<PublicGitHubCatalogOptions>(options =>
        {
            options.IsEnabled = IsPublicGitHubCatalogEnabled;
            if (!string.IsNullOrWhiteSpace(PublicGitHubApiKey))
            {
                options.GitHubApiToken = PublicGitHubApiKey;
            }
        });

        services.Configure<PrivateGitHubCatalogOptions>(options =>
        {
            options.IsEnabled = IsPrivateGitHubCatalogEnabled;
            if (!string.IsNullOrWhiteSpace(PrivateGitHubApiKey))
            {
                options.GitHubApiToken = PrivateGitHubApiKey;
            }

            PrivateGitHubCatalogCoordinates.Apply(options, PrivateGitHubCatalogOwner,
                PrivateGitHubCatalogRepositoryName, PrivateGitHubCatalogBranch, PrivateGitHubCatalogPagesUri);
        });

        var serviceProvider = services.BuildServiceProvider();

        // AB#5412: log the EFFECTIVE private catalog. Resolving the options here also forces the
        // Configure delegate above to run before the first catalog call, so a coordinate typo shows up
        // in the log next to the publish target instead of as a puzzling 404 later.
        var privateCatalogOptions = serviceProvider.GetRequiredService<IOptions<PrivateGitHubCatalogOptions>>().Value;
        Log.LogMessage(MessageImportance.High, "Private GitHub catalog: {0} (enabled: {1})",
            PrivateGitHubCatalogCoordinates.Describe(privateCatalogOptions), privateCatalogOptions.IsEnabled);

        if (!CkCompileGate.TryGetBaselineSource(CompatibilityBaseline, IsContinuousIntegration(),
                out var baselineSource))
        {
            Log.LogError("OctoCkCompatibilityBaseline must be 'Local' or 'Remote', but is '{0}'.",
                CompatibilityBaseline);
            return false;
        }

        var compileGate = serviceProvider.GetRequiredService<CkCompileGate>();
        var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
        var catalogService = serviceProvider.GetRequiredService<ICatalogService>();
        var ckSerializer = serviceProvider.GetRequiredService<ICkSerializer>();

        var modelResolver = serviceProvider.GetRequiredService<ICatalogModelResolver>();
        var contentGenerator = serviceProvider.GetRequiredService<IContentGenerator>();
        var mermaidGenerator = serviceProvider.GetRequiredService<IMermaidGenerator>();

        var compiledModelFiles = new List<string>();
        var cacheFiles = new List<string>();
        // Fail-fast for hung remote-catalog calls. HttpClient's own default is 100 s per request,
        // but a stuck dependency chain (multiple sequential GitHub fetches) can still chain past that.
        // 5 minutes is generous for the slowest healthy first-time fetch we've seen.
        const int taskTimeoutMs = 5 * 60 * 1000;
        try
        {
            var task = Task.Run(async () =>
            {
                Log.LogMessage(MessageImportance.High, "Using construction kit compiler located at '{0}'",
                    compilerService.GetType().Assembly.Location);
                var localCatalogOptions =
                    serviceProvider.GetRequiredService<IOptions<LocalFileSystemCatalogOptions>>().Value;
                Log.LogMessage(MessageImportance.High,
                    "Local file system catalog root: '{0}' (enabled: {1})",
                    localCatalogOptions.RootPath, localCatalogOptions.IsEnabled);
                foreach (var constructionKitFolder in ConstructionKitFolders)
                {
                    var operationResult = new OperationResult();
                    try
                    {
                        var constructionKitFolderPath = constructionKitFolder.GetMetadata("FullPath");

                        if (!Directory.Exists(constructionKitFolderPath))
                        {
                            Log.LogMessage(MessageImportance.High, "Creating new construction kit model in '{0}'",
                                constructionKitFolderPath);
                            await compilerService.CreateNewAsync(constructionKitFolderPath);
                        }
                        else
                        {
                            if (RefreshRemoteCatalogs)
                            {
                                Log.LogMessage(MessageImportance.High,
                                    "Refreshing construction kit model library cache");
                                await catalogService.RefreshAllCatalogCachesAsync();
                            }
                            else
                            {
                                Log.LogMessage(MessageImportance.High,
                                    "Skipping remote catalog refresh (OctoRefreshRemoteCatalogs=false). " +
                                    "Construction kit dependencies must resolve from LocalFileSystemCatalog only.");
                            }

                            Log.LogMessage(MessageImportance.High, "Compiling construction kit model in '{0}'",
                                constructionKitFolderPath);
                            var compileResult = await compilerService.CompileAsync(
                                constructionKitFolderPath,
                                OutputPath, CacheFilePath, operationResult);
                            compiledModelFiles.Add(compileResult.CompiledModelFile);
                            if (compileResult.CompiledModelCacheFilePath != null)
                            {
                                cacheFiles.Add(compileResult.CompiledModelCacheFilePath);
                            }

                            // AB#6294: compatibility gate (concept §4.3.3, gate 1) — after compiling and BEFORE any
                            // PublishAsync; it never writes to a catalog. Runs whether or not the model is published.
                            if (!await RunCompatibilityGateAsync(compileGate, ckSerializer, compileResult.CompiledModelFile,
                                    constructionKitFolderPath, baselineSource, operationResult))
                            {
                                return;
                            }

                            if (PublishCkModel)
                            {
                                OriginFileResolver originFileResolver = new(compileResult.CompiledModelFile);
                                Log.LogMessage(MessageImportance.High,
                                    $"Publishing construction kit model from '{constructionKitFolderPath}' to '{PublishCatalogName}'");
#if NETSTANDARD2_0
                                using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#else
                                await using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#endif

                                var ckCompiledModelRoot =
                                    await ckSerializer.DeserializeCompiledModelRootAsync(streamReader,
                                        compileResult.CompiledModelFile,
                                        operationResult);
                                if (operationResult.HasErrors || operationResult.HasFatalErrors)
                                {
                                    Log.LogError("Error loading model \'{FilePath}\'", compileResult.CompiledModelFile);
                                    LogOperationResults(operationResult);
                                    return;
                                }

                                // Pre-publish dependency check: refuse to publish a model whose pinned
                                // dependencies cannot be located in any registered catalog. Without this,
                                // a downstream model (e.g. Industry.Energy) can be published with a
                                // dependency on an unpublished sibling version (e.g. Industry.Basic-2.1.1)
                                // and leave the catalog graph permanently inconsistent — which the asset-
                                // repo LibraryStatus endpoint and dependency resolver both stumble over.
                                var missingDeps = new List<string>();
                                foreach (var dep in ckCompiledModelRoot.Dependencies ?? Enumerable.Empty<CkModelId>())
                                {
                                    if (!await catalogService.IsExistingAsync(dep))
                                    {
                                        missingDeps.Add(dep.FullName);
                                    }
                                }

                                if (missingDeps.Count > 0)
                                {
                                    Log.LogError(
                                        $"Refusing to publish '{ckCompiledModelRoot.ModelId}' to '{PublishCatalogName}': "
                                        + $"the following pinned dependencies are not available in any registered catalog: "
                                        + $"{string.Join(", ", missingDeps)}. "
                                        + "Either publish the missing dependency version first, or rebuild this model "
                                        + "against a dependency version that is published.");
                                    return;
                                }

                                // AB#5453: pass the task's own OperationResult. This task clears every
                                // logging provider, so resolve messages that only reach the engine's
                                // ILogger are lost; routed through the result they are rendered by the
                                // finally-block's LogOperationResults as MSBuild errors, which is what
                                // makes Execute() return false. PublishAsync additionally throws now, so
                                // the "published" line below cannot be reached for a model that is not
                                // in the catalog.
                                await catalogService.PublishAsync(PublishCatalogName, ckCompiledModelRoot,
                                    originFileResolver, true, operationResult);
                                Log.LogMessage(MessageImportance.High,
                                    $"Construction kit model published to '{PublishCatalogName}'");

                                // Always publish to LocalFileSystemCatalog as well so that
                                // dependent projects in the same solution build can resolve
                                // the freshly compiled model (LocalFileSystem has higher
                                // priority than GitHub catalogs).
                                if (IsLocalCatalogEnabled &&
                                    !string.Equals(PublishCatalogName, LocalFileSystemCatalog.Name,
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    // Deliberately handled exactly like the remote publish: a model that
                                    // cannot be resolved must not enter ANY catalog. A silently missing
                                    // local entry surfaces later as a confusing "unknown model" while a
                                    // sibling project compiles, far from its cause.
                                    await catalogService.PublishAsync(LocalFileSystemCatalog.Name,
                                        ckCompiledModelRoot, originFileResolver, true, operationResult);
                                    Log.LogMessage(MessageImportance.High,
                                        $"Construction kit model also published to '{LocalFileSystemCatalog.Name}'");
                                }
                            }

                            if (GenerateCkDocumentation)
                            {
                                Log.LogMessage(MessageImportance.High,
                                    $"Generating construction kit model Documentation to {OutputPath}");
#if NETSTANDARD2_0
                                using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#else
                                await using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#endif
                                var ckCompiledModelRoot =
                                    await ckSerializer.DeserializeCompiledModelRootAsync(streamReader,
                                        compileResult.CompiledModelFile,
                                        operationResult);
                                if (operationResult.HasErrors || operationResult.HasFatalErrors)
                                {
                                    Log.LogError("Error loading model \'{FilePath}\'", compileResult.CompiledModelFile);
                                    LogOperationResults(operationResult);
                                    return;
                                }

                                var originFileResolver = new OriginFileResolver(compileResult.CompiledModelFile);
                                var modelGraph = await modelResolver.HardResolveAsync(ckCompiledModelRoot,
                                    originFileResolver,
                                    operationResult);

                                var path = MmPath.NormalizePath(OutputPath);

                                await mermaidGenerator.GenerateMermaidTextOutput(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateVersionHistory(path, ckCompiledModelRoot.ModelId,
                                    ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);

                                await contentGenerator.GenerateAttributesMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateEnumsMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateRecordsMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateTypesMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),

                                    CkLinkPath);
                                await contentGenerator.GenerateAssociationRolesMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateInterfacesMarkdownTable(modelGraph, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.Version.ToString());
                            }
                        }

                        Log.LogMessage(MessageImportance.Normal, "Finished");
                    }
                    catch (ModelValidationException ex)
                    {
                        // The messages normally live in operationResult and are rendered by the finally
                        // block below; swallowing the exception keeps the remaining folders going.
                        ReportUnreportedFailure(operationResult, ex);
                    }
                    catch (CompilerException ex)
                    {
                        ReportUnreportedFailure(operationResult, ex);
                    }
                    finally
                    {
                        LogOperationResults(operationResult);
                    }
                }
            });
            if (!task.Wait(taskTimeoutMs))
            {
                Log.LogError(
                    "Construction kit compile did not complete within {0} minutes. This usually means a remote " +
                    "catalog (PublicGitHub/PrivateGitHub) call is hanging. Verify network connectivity to " +
                    "api.github.com and that the catalog API tokens are not rate-limited.",
                    taskTimeoutMs / 60_000);
                return false;
            }

            CompiledModelFiles = compiledModelFiles.ToArray();
            CacheFiles = cacheFiles.ToArray();
        }
        catch (AggregateException ae)
            when (ae.GetBaseException() is TaskCanceledException or OperationCanceledException)
        {
            // HttpClient's per-request timeout fires as TaskCanceledException; surface it as actionable rather
            // than dumping a 40-frame async stack with the message "A task was canceled".
            Log.LogError(
                "Construction kit compile was canceled — likely an HttpClient timeout against a remote " +
                "catalog (PublicGitHub/PrivateGitHub). Verify network connectivity to api.github.com and " +
                "that OctoPrivateGitHubApiKey / OctoPublicGitHubApiKey are not rate-limited.");
            return false;
        }
        catch (Exception ex)
        {
            // This logging helper method is designed to capture and display information
            // from arbitrary exceptions in a standard way.
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }

        return !Log.HasLoggedErrors;
    }

    private static bool IsContinuousIntegration() =>
        IsTrue(Environment.GetEnvironmentVariable("TF_BUILD")) ||
        IsTrue(Environment.GetEnvironmentVariable("ContinuousIntegrationBuild"));

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     AB#6294: runs the compile gate on the freshly compiled model and renders its messages into the build log.
    ///     Returns false when the build has to fail (nothing is published then).
    /// </summary>
    private async Task<bool> RunCompatibilityGateAsync(CkCompileGate compileGate, ICkSerializer ckSerializer,
        string compiledModelFile, string constructionKitFolderPath, CkBaselineSource baselineSource,
        OperationResult operationResult)
    {
#if NETSTANDARD2_0
        using var stream = File.OpenRead(compiledModelFile);
#else
        await using var stream = File.OpenRead(compiledModelFile);
#endif
        var compiled = await ckSerializer.DeserializeCompiledModelRootAsync(stream, compiledModelFile,
            operationResult);
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            Log.LogError("Error loading model \'{0}\'", compiledModelFile);
            LogOperationResults(operationResult);
            return false;
        }

        var result = await compileGate.RunAsync(compiled, baselineSource, constructionKitFolderPath);
        var metadataFile = Path.Combine(constructionKitFolderPath, "ckModel.yaml");
        foreach (var message in result.Messages)
        {
            if (message.Severity == CkCompileGateSeverity.Error)
            {
                Log.LogError(null, message.Code, null, metadataFile, 0, 0, 0, 0, message.Text);
            }
            else if (message.Code != null)
            {
                Log.LogMessage(null, message.Code, null, metadataFile, 0, 0, 0, 0, MessageImportance.High,
                    message.Text, null);
            }
            else
            {
                Log.LogMessage(MessageImportance.High, message.Text);
            }
        }

        return !result.HasErrors;
    }

    /// <summary>
    ///     AB#5453 safety net: the two catch blocks above were empty on the assumption that the
    ///     <see cref="OperationResult" /> always carries the reason, and <c>Execute</c> returns
    ///     <c>!Log.HasLoggedErrors</c>. When that assumption does not hold the build went green on a
    ///     failed step. Log an error exactly when nothing else will, so the build fails either way
    ///     without double-reporting what <see cref="LogOperationResults" /> already prints.
    /// </summary>
    private void ReportUnreportedFailure(OperationResult operationResult, Exception exception)
    {
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            return;
        }

        Log.LogError("Construction kit step failed: {0}", exception.Message);
    }

    private void LogOperationResults(OperationResult operationResult)
    {
        foreach (var operationResultMessage in operationResult.Messages)
        {
            switch (operationResultMessage.MessageLevel)
            {
                case MessageLevel.FatalError:
                case MessageLevel.Error:
                    Log.LogError(null, operationResultMessage.MessageNumber.ToString(), null,
                        operationResultMessage.Location, 0, 0, 0, 0,
                        operationResultMessage.MessageText);
                    break;
                case MessageLevel.Warning:
                    Log.LogWarning(null, operationResultMessage.MessageNumber.ToString(), null,
                        operationResultMessage.Location, 0, 0, 0, 0,
                        operationResultMessage.MessageText);
                    break;
                default:
                    Log.LogMessage(null, operationResultMessage.MessageNumber.ToString(), null,
                        operationResultMessage.Location, 0, 0, 0, 0,
                        MessageImportance.High,
                        operationResultMessage.MessageText, null);
                    break;
            }
        }
    }
}