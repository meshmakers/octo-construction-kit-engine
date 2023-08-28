using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal class PublishCommand : Command<OctoToolOptions>
{
    private readonly ICkModelRepositoryManager _ckModelRepositoryManager;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkValidationService _ckValidationService;
    private readonly IArgument _pathArg;
    private readonly IArgument _repositoryArg;
    private readonly IArgument _forceArg;

    public PublishCommand(ILogger<PublishCommand> logger, IOptions<OctoToolOptions> options,
        ICkModelRepositoryManager ckModelRepositoryManager, ICkSerializer ckSerializer, ICkValidationService ckValidationService)
        : base(logger, "Publish", "Publish a compiled construction kit to a repository", options)
    {
        _ckModelRepositoryManager = ckModelRepositoryManager;
        _ckSerializer = ckSerializer;
        _ckValidationService = ckValidationService;

        _pathArg = CommandArgumentValue.AddArgument("f", "file",
            new[] { "Path of compiled construction kit model file" }, true, 1);
        
        _repositoryArg = CommandArgumentValue.AddArgument("rep", "repository",
            new[] { "Name of the construction kit repository. By default 'LocalRepository' is used." }, 1);
        
        _forceArg = CommandArgumentValue.AddArgument("r", "replace",
            new[] { "Replaces construction kits models that may exists in repo." }, 0);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Publish construction kit model");

        var filePath = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var repositoryName = CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_repositoryArg) ?? "LocalRepository";
        var isForced = CommandArgumentValue.IsArgumentUsed(_forceArg);
        Logger.LogInformation("Path of compiled construction kit file: {FilePath}", filePath);
        Logger.LogInformation("Repository '{Repository}'", repositoryName);
        
        var operationResult = new OperationResult();
        await using var streamReader = File.OpenRead(filePath);
        try
        {
            var ckCompiledModelRoot = await _ckSerializer.DeserializeCompiledModelRootAsync(streamReader, operationResult);
            if (operationResult.HasErrors)
            {
                Logger.LogError("Error loading model \'{FilePath}\'", filePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }
            
            await _ckValidationService.ValidateAsync(ckCompiledModelRoot, operationResult);
            if (operationResult.HasErrors)
            {
                Logger.LogError("Error validating model \'{FilePath}\'", filePath);
                operationResult.WriteMessagesToLogger(Logger);
                return;
            }
        
            await _ckModelRepositoryManager.PublishModelAsync(repositoryName, ckCompiledModelRoot, isForced);
        
            Logger.LogInformation("Construction kit model published");
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
