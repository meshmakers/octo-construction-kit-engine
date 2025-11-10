using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        });

        var serviceProvider = services.BuildServiceProvider();

        var ckModelRepositoryService = serviceProvider.GetRequiredService<ICatalogService>();

        var compiledModelFiles = new List<string>();
        var cacheFiles = new List<string>();
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
                            Log.LogMessage(MessageImportance.High,
                                "Restoring construction kit model libraries in '{0}'",
                                ckModelConfigFilePath);
                            var compileResult = await ckModelRepositoryService.RestoreConstructionKitModelsAsync(
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
            task.Wait();

            CompiledModelFiles = compiledModelFiles.ToArray();
            CacheFiles = cacheFiles.ToArray();
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