using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to validate a blueprint directory.
/// </summary>
internal class ValidateCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintDraftValidator _draftValidator;
    private readonly IArgument _pathArg;
    private readonly IArgument _ckCatalogsArg;

    public ValidateCommand(
        ILogger<ValidateCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintDraftValidator draftValidator)
        : base(logger, "validate", "Validates a blueprint directory", options)
    {
        _draftValidator = draftValidator;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path to the blueprint directory"], true, 1);
        _ckCatalogsArg = BpmDraftValidation.AddCkCatalogsArgument(CommandArgumentValue);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Validating blueprint");

        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        Logger.LogInformation("Path: {Path}", path);

        // AB#9371: the shared draft validator; every finding is logged with its stable code.
        var result = await BpmDraftValidation.RunAsync(_draftValidator, path,
            BpmDraftValidation.ReadCkCatalogs(CommandArgumentValue, _ckCatalogsArg), Logger);
        var blueprintMeta = result.BlueprintMeta!;

        Logger.LogInformation("Blueprint '{BlueprintId}' is valid", blueprintMeta.BlueprintId);
        Logger.LogInformation("  Description: {Description}", blueprintMeta.Description);
        Logger.LogInformation("  CK Dependencies: {Count}",
            blueprintMeta.CkModelDependencies?.Count ?? 0);

        var seedDataPaths = BlueprintSeedData.ResolvePaths(blueprintMeta);
        if (seedDataPaths.Count > 0)
        {
            Logger.LogInformation("  Seed Data: {SeedDataFileCount} file(s)", seedDataPaths.Count);
            foreach (var seedDataPath in seedDataPaths)
            {
                Logger.LogInformation("    - {SeedDataPath}", seedDataPath);
            }
        }
    }
}
