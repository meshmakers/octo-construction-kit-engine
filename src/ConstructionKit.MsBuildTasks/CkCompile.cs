using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable MemberCanBePrivate.Global

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
    /// When true, the construction kit folder is compiled
    /// </summary>
    [Required]
    public bool Compile { get; set; } = false;

    /// <summary>
    /// When true, a cache file is created parallel to the compiled construction kit model containing all dependencies
    /// </summary>
    [Required]
    public bool CreateCacheFile { get; set; } = false;
    
    /// <summary>
    /// When true, the compiled construction kit model is published to the local repository
    /// </summary>
    [Required]
    public bool PublishCkModel { get; set; } = true;

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
        var ckModelRepositoryService = _serviceProvider.GetRequiredService<ICkModelRepositoryService>();
        var ckSerializer = _serviceProvider.GetRequiredService<ICkSerializer>();
        
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

                        if (!Directory.Exists(constructionKitFolderPath))
                        {
                            Log.LogMessage(MessageImportance.High, "Creating new construction kit model in '{0}'", constructionKitFolderPath);
                            await compilerService.CreateNewAsync(constructionKitFolderPath);
                        }
                        else
                        {
                            CompileResult? compileResult;
                            if (Compile)
                            {
                                Log.LogMessage(MessageImportance.High, "Compiling construction kit model in '{0}'", constructionKitFolderPath);
                                compileResult = await compilerService.CompileAsync(constructionKitFolderPath, CreateCacheFile);
                                compiledModelFiles.Add(compileResult.CompiledModelFile);
                                if (compileResult.CompiledModelCacheFilePath != null)
                                {
                                    cacheFiles.Add(compileResult.CompiledModelCacheFilePath);
                                }
                            }
                            else 
                            {
                                Log.LogMessage(MessageImportance.High, "Getting information about construction kit model in '{0}'", constructionKitFolderPath);
                                compileResult = await compilerService.GetConstructionKitFolderInfoAsync(constructionKitFolderPath, CreateCacheFile);
                                compiledModelFiles.Add(compileResult.CompiledModelFile);
                                if (compileResult.CompiledModelCacheFilePath != null)
                                {
                                    cacheFiles.Add(compileResult.CompiledModelCacheFilePath);
                                }
                            }  
                            
                            if (PublishCkModel)
                            {
                                Log.LogMessage(MessageImportance.High, "Publishing construction kit model to 'LocalRepository'");
                                var operationResult = new OperationResult();
                                await using var streamReader = File.OpenRead(compileResult.CompiledModelFile);

                                var ckCompiledModelRoot =
                                    await ckSerializer.DeserializeCompiledModelRootAsync(streamReader, compileResult.CompiledModelFile, operationResult);
                                if (operationResult.HasErrors || operationResult.HasFatalErrors)
                                {
                                    Log.LogError("Error loading model \'{FilePath}\'", compileResult.CompiledModelFile);
                                    LogOperationResults(operationResult);
                                    return;
                                }

                                await ckModelRepositoryService.PublishModelAsync("LocalRepository", ckCompiledModelRoot, true);
                                Log.LogMessage(MessageImportance.High,"Construction kit model published to 'LocalRepository'");
                            } 
                        }
                    }

                    Log.LogMessage(MessageImportance.High, "Finished");
                }
                catch (CompilerException ex)
                {
                    LogOperationResults(ex.OperationResult);
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
}