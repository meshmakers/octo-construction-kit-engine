using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class GetCommand : Command<OctoToolOptions>
{
    private readonly ICatalogService _catalogService;
    private readonly IArgument _repositoryArg;
    private readonly IArgument _ckModelNameArg;
    private readonly IArgument _filePathArg;
    private readonly ICkSerializer _ckSerializer;

    public GetCommand(ILogger<GetCommand> logger, IOptions<OctoToolOptions> options,
        ICatalogService catalogService, ICkSerializer ckSerializer)
        : base(logger, "Get", "Gets a construction kit library model from the given Construction Kit catalog", options)
    {
        _catalogService = catalogService;
        _ckSerializer = ckSerializer;

        _repositoryArg = CommandArgumentValue.AddArgument("c", "catalog",
            ["Name of the construction kit catalog. By default 'LocalFileSystemCatalog' is used."], true, 1);
        _ckModelNameArg = CommandArgumentValue.AddArgument("n", "name",
            ["Name of the construction kit model to get."], true, 1);
        _filePathArg = CommandArgumentValue.AddArgument("f", "file",
            ["If set, the construction kit model is stored at the given file path."], false, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Construction Kit Model Repositories:");

        var repositoryName = CommandArgumentValue.GetArgumentScalarValue<string>(_repositoryArg);
        var ckModelId = CommandArgumentValue.GetArgumentScalarValue<string>(_ckModelNameArg);

        Logger.LogInformation("Repository '{Repository}'", repositoryName);
        Logger.LogInformation("Construction Kit Model '{CkModelId}'", ckModelId);
        OperationResult operationResult = new();
        var result = await _catalogService.GetAsync(repositoryName, ckModelId, operationResult);

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            // AB#5453 (same failure mode as Publish): returning here left the exit code at 0.
            // The 'model not found' case below stays a warning with exit code 0 — that is an answer,
            // not a failure.
            Logger.LogError("Error looking up model configuration '{CkModelId}' in repository '{Repository}'", ckModelId, repositoryName);
            throw new CompilerException(
                $"Construction kit model '{ckModelId}' could not be read from repository '{repositoryName}'.",
                operationResult);
        }

        if (result == null)
        {
            Logger.LogWarning("Construction Kit Model '{CkModelId}' not found in repository '{Repository}'", ckModelId, repositoryName);
            return;
        }
        Logger.LogInformation("Found Construction Kit Model '{CkModelId}' in repository '{Repository}'", ckModelId, repositoryName);
        if (CommandArgumentValue.IsArgumentUsed(_filePathArg))
        {
            var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_filePathArg);
            await using var streamWriter = new StreamWriter(filePath);
            await _ckSerializer.SerializeAsync(streamWriter, result);
            Logger.LogInformation("Stored Construction Kit Model '{CkModelId}' to file '{FilePath}'", ckModelId, filePath);
        }
    }
}