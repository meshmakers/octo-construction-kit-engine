using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
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
    /// When true, the compiled construction kit model is published to the local repository
    /// </summary>
    [Required]
    public bool PublishCkModel { get; set; } = true;
    
    /// <summary>
    /// When true, the compiled construction kit model is generated as .md files to the local repository
    /// </summary>
    [Required]
    public bool GenerateCkDocumentation { get; set; } = true;

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
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddConstructionKit();
        services.AddDocumentationService();
        
        var serviceProvider = services.BuildServiceProvider();

        var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
        var ckModelRepositoryService = serviceProvider.GetRequiredService<ICkModelRepositoryService>();
        var ckSerializer = serviceProvider.GetRequiredService<ICkSerializer>();
        
        var modelResolver = serviceProvider.GetRequiredService<IModelResolver>();
        var contentGenerator = serviceProvider.GetRequiredService<IContentGenerator>();
        var mermaidGenerator = serviceProvider.GetRequiredService<IMermaidGenerator>();

        var compiledModelFiles = new List<string>();
        var cacheFiles = new List<string>();
        try
        {
            var task = Task.Run(async () =>
            {
                Log.LogMessage(MessageImportance.High, "Using construction kit compiler located at '{0}'",
                    compilerService.GetType().Assembly.Location);
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
                            Log.LogMessage(MessageImportance.High, "Compiling construction kit model in '{0}'",
                                constructionKitFolderPath);
                            var compileResult = await compilerService.CompileAsync(constructionKitFolderPath.GetOsSpecificPath(),
                                OutputPath.GetOsSpecificPath(), CacheFilePath?.GetOsSpecificPath(), operationResult);
                            compiledModelFiles.Add(compileResult.CompiledModelFile);
                            if (compileResult.CompiledModelCacheFilePath != null)
                            {
                                cacheFiles.Add(compileResult.CompiledModelCacheFilePath);
                            }

                            if (PublishCkModel)
                            {
                                Log.LogMessage(MessageImportance.High,
                                    "Publishing construction kit model to 'LocalRepository'");
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

                                await ckModelRepositoryService.PublishModelAsync("LocalRepository", ckCompiledModelRoot,
                                    true);
                                Log.LogMessage(MessageImportance.High,
                                    "Construction kit model published to 'LocalRepository'");
                            }

                            if (GenerateCkDocumentation)
                            {
                                Log.LogMessage(MessageImportance.High,
                                    $"Generating construction kit model Documentation to {OutputPath.GetOsSpecificPath()}");
#if NETSTANDARD2_0
                                using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#else
                                await using var streamReader = File.OpenRead(compileResult.CompiledModelFile);
#endif
                                var path = OutputPath.GetOsSpecificPath();
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
                                var resolvedTypes = await modelResolver.ResolveAsync(ckCompiledModelRoot, originFileResolver, 
                                    operationResult);
                                
                                await mermaidGenerator.GenerateMermaidTextOutput(resolvedTypes, path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), 
                                    CkLinkPath);
                                await contentGenerator.GenerateVersionHistory(path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), 
                                    CkLinkPath);

                                await contentGenerator.GenerateAttributesMarkdownTable(resolvedTypes, path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), 
                                    CkLinkPath);
                                await contentGenerator.GenerateEnumsMarkdownTable(resolvedTypes, path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(),
                                    CkLinkPath);
                                await contentGenerator.GenerateRecordsMarkdownTable(resolvedTypes, path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), 
                                    CkLinkPath);
                                await contentGenerator.GenerateTypesMarkdownTable(resolvedTypes, path, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), 
                                    CkLinkPath);
                                await contentGenerator.GenerateAssociationRolesMarkdownTable(resolvedTypes, path,
                                    ckCompiledModelRoot.ModelId, ckCompiledModelRoot.ModelId.ModelVersion.ToString(), CkLinkPath);
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