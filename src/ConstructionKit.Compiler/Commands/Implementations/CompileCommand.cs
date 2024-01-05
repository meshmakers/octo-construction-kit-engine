using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class CompileCommand : Command<OctoToolOptions>
{
    private readonly IArgument _cacheArg;
    private readonly ICkModelRepositoryService _ckModelRepositoryService;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICompilerService _compilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _publishArg;

    public CompileCommand(ILogger<CompileCommand> logger, IOptions<OctoToolOptions> options,
        ICompilerService compilerService, ICkSerializer ckSerializer, ICkModelRepositoryService ckModelRepositoryService)
        : base(logger, "Compile", "Validates and creates output files for a construction kit model directory", options)
    {
        _compilerService = compilerService;
        _ckSerializer = ckSerializer;
        _ckModelRepositoryService = ckModelRepositoryService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            new[] { "Root path of construction kit model directory" }, true, 1);

        _cacheArg = CommandArgumentValue.AddArgument("c", "cache",
            new[]
            {
                "If used, parallel to the compiled construction kit model a cache file is created containing " +
                "all dependent construction kit models."
            }, false);

        _publishArg = CommandArgumentValue.AddArgument("i", "import",
            new[] { "When set, the compiled file gets imported to the local repository." }, false);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Compiling construction kit model directory");

        var rootPath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var createCacheFile = CommandArgumentValue.IsArgumentUsed(_cacheArg);
        var doPublish = CommandArgumentValue.IsArgumentUsed(_publishArg);
        Logger.LogInformation("Path of root directory: {Path}", Path.GetFullPath(rootPath));

        var compiledModelFilePath = await _compilerService.CompileAsync(rootPath, createCacheFile);

        if (doPublish)
        {
            Logger.LogInformation("Publishing construction kit model to 'LocalRepository'");
            var operationResult = new OperationResult();
            await using var streamReader = File.OpenRead(compiledModelFilePath);

            var ckCompiledModelRoot =
                await _ckSerializer.DeserializeCompiledModelRootAsync(streamReader, compiledModelFilePath, operationResult);
            if (operationResult.HasErrors)
            {
                Logger.LogError("Error loading model \'{FilePath}\'", compiledModelFilePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }

            await _ckModelRepositoryService.PublishModelAsync("LocalRepository", ckCompiledModelRoot, true);
            Logger.LogInformation("Construction kit model published to 'LocalRepository'");
        }

        Logger.LogInformation("Construction kit model directory compiled");
    }
}