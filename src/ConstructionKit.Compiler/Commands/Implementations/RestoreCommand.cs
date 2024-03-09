using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

public class RestoreCommand : Command<OctoToolOptions>
{
    private readonly ICkModelRepositoryService _ckModelRepositoryService;
    private readonly IArgument _pathArg;
    private readonly IArgument _outputPathArg;
    private readonly IArgument _cachePathArg;

    public RestoreCommand(ILogger<RestoreCommand> logger, IOptions<OctoToolOptions> options,
        ICkModelRepositoryService ckModelRepositoryService)
        : base(logger, "Restore", "Restores construction kits based on a construction kit model file.", options)
    {
        _ckModelRepositoryService = ckModelRepositoryService;
        _pathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of the model configuration file"], true, 1);
        _outputPathArg = CommandArgumentValue.AddArgument("o", "outputPath",
            description: ["Output path of compiled construction kit"], true, 1);

        _cachePathArg = CommandArgumentValue.AddArgument("c", "cache",
        [
            "If used, at the defined path a cache file is created containing " +
            "all dependent construction kit models."
        ], false, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Restoring construction kit models");

        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var outputPath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputPathArg);
        string? cacheFilePath = null;
        if (CommandArgumentValue.IsArgumentUsed(_cachePathArg))
        {
            cacheFilePath = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_cachePathArg);
        }

        var operationResult = new OperationResult();
        try
        {
            await _ckModelRepositoryService.RestoreConstructionKitModelsAsync(filePath, outputPath, cacheFilePath,
                operationResult);
            if (operationResult.HasErrors || operationResult.HasFatalErrors)
            {
                Logger.LogError("Error loading model configuration \'{FilePath}\'", filePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }

            Logger.LogInformation("Construction kit model configuration restored");
        }
        catch (ModelParseException)
        {
            Logger.LogError("Error loading model \'{FilePath}\'", filePath);
            operationResult.WriteMessagesToLogger(Logger);
        }
        catch (ModelValidationException)
        {
            Logger.LogError("Error validating model \'{FilePath}\'", filePath);
            operationResult.WriteMessagesToLogger(Logger);
        }
    }
}