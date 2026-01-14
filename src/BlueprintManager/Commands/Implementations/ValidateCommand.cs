using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to validate a blueprint directory.
/// </summary>
internal class ValidateCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCompilerService _blueprintCompilerService;
    private readonly IArgument _pathArg;

    public ValidateCommand(
        ILogger<ValidateCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCompilerService blueprintCompilerService)
        : base(logger, "validate", "Validates a blueprint directory", options)
    {
        _blueprintCompilerService = blueprintCompilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path to the blueprint directory"], true, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Validating blueprint");

        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        Logger.LogInformation("Path: {Path}", path);

        var operationResult = new OperationResult();

        try
        {
            var blueprintMeta = await _blueprintCompilerService.ValidateAsync(path, operationResult);

            // Log any warnings
            foreach (var message in operationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Warning))
            {
                Logger.LogWarning("{Message}", message.MessageText);
            }

            Logger.LogInformation("Blueprint '{BlueprintId}' is valid", blueprintMeta.BlueprintId);
            Logger.LogInformation("  Description: {Description}", blueprintMeta.Description);
            Logger.LogInformation("  CK Dependencies: {Count}",
                blueprintMeta.CkModelDependencies?.Count ?? 0);
            Logger.LogInformation("  Composed Blueprints: {Count}",
                blueprintMeta.ComposedBlueprints?.Count ?? 0);

            if (!string.IsNullOrEmpty(blueprintMeta.SeedDataPath))
            {
                Logger.LogInformation("  Seed Data: {SeedDataPath}", blueprintMeta.SeedDataPath);
            }
        }
        catch (BlueprintCatalogException)
        {
            foreach (var message in operationResult.Messages)
            {
                if (message.MessageLevel == MessageLevel.Error)
                {
                    Logger.LogError("{Message}", message.MessageText);
                }
                else if (message.MessageLevel == MessageLevel.Warning)
                {
                    Logger.LogWarning("{Message}", message.MessageText);
                }
            }

            throw;
        }
    }
}
