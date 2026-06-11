using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

public class RestoreCommand : Command<OctoToolOptions>
{
    private readonly ICatalogService _catalogService;
    private readonly IOptions<LocalFileSystemCatalogOptions> _localCatalogOptions;
    private readonly IArgument _pathArg;
    private readonly IArgument _outputPathArg;
    private readonly IArgument _cachePathArg;
    private readonly IArgument _localCatalogRoot;

    public RestoreCommand(ILogger<RestoreCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, IOptions<LocalFileSystemCatalogOptions> localCatalogOptions)
        : base(logger, "Restore", "Restores construction kits based on a construction kit model file.", options)
    {
        _catalogService = catalogService;
        _localCatalogOptions = localCatalogOptions;
        _pathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of the model configuration file"], true, 1);
        _outputPathArg = CommandArgumentValue.AddArgument("o", "outputPath",
            description: ["Output path of compiled construction kit"], true, 1);

        _cachePathArg = CommandArgumentValue.AddArgument("c", "cache",
        [
            "If used, at the defined path a cache file is created containing " +
            "all dependent construction kit models."
        ], false, 1);

        _localCatalogRoot = CommandArgumentValue.AddArgument("lcr", "localCatalogRoot",
            ["Root path of the local Construction Kit Library catalog for this invocation only (not persisted)"],
            false, 1);
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

        if (CommandArgumentValue.IsArgumentUsed(_localCatalogRoot))
        {
            _localCatalogOptions.Value.ApplyRootPath(
                CommandArgumentValue.GetArgumentScalarValue<string>(_localCatalogRoot));
        }

        Logger.LogInformation("Local Construction Kit catalog root: {Path}", _localCatalogOptions.Value.RootPath);

        try
        {
            var operationResult = new OperationResult();
            await _catalogService.RestoreConstructionKitModelsAsync(filePath, outputPath, cacheFilePath,
                operationResult);
            if (operationResult.HasErrors || operationResult.HasFatalErrors)
            {
                Logger.LogError("Error loading model configuration \'{FilePath}\'", filePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }
        }
        catch (Exception)
        {
            Logger.LogError("Error loading model configuration \'{FilePath}\'", filePath);
            throw;
        }

        Logger.LogInformation("Construction kit model configuration restored");
    }
}