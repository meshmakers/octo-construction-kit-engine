using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
/// Restores a construction kit using msbuild
/// </summary>
public class CkRestore : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// A list of folders containing construction kits
    /// </summary>
    [Required]
    public ITaskItem[] ConstructionKitModelConfigFiles { get; set; } = null!;

    /// <summary>
    /// When a directory defined, a cache files are created containing all dependencies
    /// </summary>
    public string? CacheFilePath { get; set; }

    /// <summary>
    /// Gets or sets the output path
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = null!;

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
    /// When true, the public GitHub catalog is consulted during dependency resolution.
    /// </summary>
    public bool IsPublicGitHubCatalogEnabled { get; set; } = true;

    /// <summary>
    /// When true, the private GitHub catalog is consulted during dependency resolution.
    /// </summary>
    public bool IsPrivateGitHubCatalogEnabled { get; set; } = true;

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
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddConstructionKit();

        services.Configure<LocalFileSystemCatalogOptions>(options =>
        {
            options.IsEnabled = IsLocalCatalogEnabled;
            options.ApplyRootPath(LocalCatalogRootPath);
        });

        services.Configure<PublicGitHubCatalogOptions>(options =>
        {
            options.IsEnabled = IsPublicGitHubCatalogEnabled;
        });

        services.Configure<PrivateGitHubCatalogOptions>(options =>
        {
            options.IsEnabled = IsPrivateGitHubCatalogEnabled;
        });

        var serviceProvider = services.BuildServiceProvider();

        var catalogService = serviceProvider.GetRequiredService<ICatalogService>();

        var localCatalogOptions =
            serviceProvider.GetRequiredService<IOptions<LocalFileSystemCatalogOptions>>().Value;
        Log.LogMessage(MessageImportance.High,
            "Local file system catalog root: '{0}' (enabled: {1})",
            localCatalogOptions.RootPath, localCatalogOptions.IsEnabled);

        var compiledModelFiles = new List<string>();
        var cacheFiles = new List<string>();
        // See CkCompile.Execute for the rationale on this fail-fast.
        const int taskTimeoutMs = 5 * 60 * 1000;
        try
        {
            var task = Task.Run(async () =>
            {
                foreach (var item in ConstructionKitModelConfigFiles)
                {
                    var operationResult = new OperationResult();
                    try
                    {
                        var ckModelConfigFilePath = item.GetMetadata("FullPath");

                        if (File.Exists(ckModelConfigFilePath))
                        {
                            Log.LogMessage(MessageImportance.High, "Refreshing construction kit model library cache");
                            await catalogService.RefreshAllCatalogCachesAsync();

                            Log.LogMessage(MessageImportance.High,
                                "Restoring construction kit model libraries in '{0}'",
                                ckModelConfigFilePath);
                            var compileResult = await catalogService.RestoreConstructionKitModelsAsync(
                                ckModelConfigFilePath,
                                OutputPath, CacheFilePath, operationResult);
                            if (operationResult.HasErrors || operationResult.HasFatalErrors)
                            {
                                Log.LogError("Error restoring model \'{FilePath}\'", ckModelConfigFilePath);
                                LogOperationResults(operationResult);
                                return;
                            }

                            foreach (var result in compileResult)
                            {
                                compiledModelFiles.Add(result.CompiledModelFile);
                                if (result.CompiledModelCacheFilePath != null)
                                {
                                    cacheFiles.Add(result.CompiledModelCacheFilePath);
                                }
                            }
                        }

                        Log.LogMessage(MessageImportance.Normal, "Finished");
                    }
                    catch (CompilerException)
                    {
                        // Left blank intentionally
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
                    "Construction kit restore did not complete within {0} minutes. This usually means a remote " +
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
            Log.LogError(
                "Construction kit restore was canceled — likely an HttpClient timeout against a remote " +
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