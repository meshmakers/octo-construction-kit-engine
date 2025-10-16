using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class PublishCommand : CkcCommand
{
    private readonly ICatalogService _catalogService;
    private readonly ICkSerializer _ckSerializer;
    private readonly IArgument _forceArg;
    private readonly IArgument _pathArg;
    private readonly IArgument _repositoryArg;

    public PublishCommand(ILogger<PublishCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, ICkSerializer ckSerializer)
        : base(logger, "Publish", "Publish a compiled construction kit to a catalog", options)
    {
        _catalogService = catalogService;
        _ckSerializer = ckSerializer;

        _pathArg = CommandArgumentValue.AddArgument("f", "file",
            ["Path of compiled construction kit model file"], true, 1);

        _repositoryArg = CommandArgumentValue.AddArgument("c", "catalog",
            ["Name of the construction kit catalog. By default 'LocalFileSystemCatalog' is used."], 1);

        _forceArg = CommandArgumentValue.AddArgument("r", "replace",
            ["Replaces construction kits models that may exists in repo."], 0);
    }

    public override async Task Execute()
    {
        await base.Execute();

        Logger.LogInformation("Publish construction kit model");

        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var repositoryName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_repositoryArg) ??
                             "LocalFileSystemCatalog";
        var isForced = CommandArgumentValue.IsArgumentUsed(_forceArg);
        Logger.LogInformation("Path of compiled construction kit file: {FilePath}", filePath);
        Logger.LogInformation("Repository '{Repository}'", repositoryName);

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver(filePath);
        await using var streamReader = File.OpenRead(filePath);
        try
        {
            var ckCompiledModelRoot =
                await _ckSerializer.DeserializeCompiledModelRootAsync(streamReader, filePath, operationResult);
            if (operationResult.HasErrors)
            {
                Logger.LogError("Error loading model \'{FilePath}\'", filePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }

            await _catalogService.PublishAsync(repositoryName, ckCompiledModelRoot, originFileResolver, isForced);

            Logger.LogInformation("Construction kit model published");
        }
        catch (Exception)
        {
            Logger.LogError("Error publishing model \'{FilePath}\'", filePath);
            throw;
        }
    }
}