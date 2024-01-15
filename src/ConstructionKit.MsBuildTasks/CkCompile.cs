using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
/// Compiles a construction kit using msbuild
/// </summary>
public class CkCompile : Microsoft.Build.Utilities.Task
{
    private readonly ServiceProvider _serviceProvider;

    public CkCompile()
    {
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddConstructionKit();
        _serviceProvider = services.BuildServiceProvider();
    }


    /// <summary>
    /// A list of folders containing construction kits
    /// </summary>
    [Required]
    public ITaskItem[] ConstructionKitFolders { get; set; } = null!;

    /// <summary>
    /// When true, a cache file is created parallel to the compiled construction kit model containing all dependencies
    /// </summary>
    [Required]
    public bool CreateCacheFile { get; set; } = false;

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
        var compilerService = _serviceProvider.GetRequiredService<ICompilerService>();

        var compiledModelFiles = new List<string>();
        var cacheFiles = new List<string>();
        try
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    foreach (var constructionKitFolder in ConstructionKitFolders)
                    {
                        var constructionKitFolderPath = constructionKitFolder.GetMetadata("FullPath");

                        var file = await compilerService.CompileAsync(constructionKitFolderPath, CreateCacheFile);
                        compiledModelFiles.Add(file.CompiledModelFile);
                        if (file.CompiledModelCacheFilePath != null)
                        {
                            cacheFiles.Add(file.CompiledModelCacheFilePath);
                        }
                    }

                    Log.LogMessage(MessageImportance.High, "Finished");
                }
                catch (CompilerException ex)
                {
                    foreach (var operationResultMessage in ex.OperationResult.Messages)
                    {
                        switch (operationResultMessage.MessageLevel)
                        {
                            case MessageLevel.FatalError:
                            case MessageLevel.Error:
                                Log.LogError(null, operationResultMessage.MessageNumber.ToString(), null, null, 0, 0, 0, 0,
                                    operationResultMessage.MessageText);
                                break;
                            case MessageLevel.Warning:
                                Log.LogWarning(null, operationResultMessage.MessageNumber.ToString(), null, null, 0, 0, 0, 0,
                                    operationResultMessage.MessageText);
                                break;
                            default:
                                Log.LogMessage(null, operationResultMessage.MessageNumber.ToString(), null, null, 0, 0, 0, 0, MessageImportance.High, 
                                    operationResultMessage.MessageText, null);
                                break;
                        }
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
}