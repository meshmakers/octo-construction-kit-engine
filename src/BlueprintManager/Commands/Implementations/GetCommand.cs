using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to get a blueprint from a catalog.
/// </summary>
internal class GetCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCatalogManager _catalogManager;
    private readonly IArgument _blueprintArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _outputArg;

    public GetCommand(
        ILogger<GetCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager)
        : base(logger, "get", "Gets a blueprint from a catalog", options)
    {
        _catalogManager = catalogManager;

        _blueprintArg = CommandArgumentValue.AddArgument("b", "blueprint",
            ["Blueprint ID to get (e.g., 'MyBlueprint-1.0.0')"], true, 1);

        _catalogArg = CommandArgumentValue.AddArgument("c", "catalog",
            ["Name of the blueprint catalog (searches all if not specified)"], false, 1);

        _outputArg = CommandArgumentValue.AddArgument("o", "output",
            ["Output directory to copy the blueprint to"], false, 1);
    }

    public override async Task Execute()
    {
        var blueprintIdString = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintArg);
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg)
            : null;

        var blueprintId = new BlueprintId(blueprintIdString);

        Logger.LogInformation("Looking up blueprint '{BlueprintId}'", blueprintId.FullName);

        if (!string.IsNullOrEmpty(catalogName))
        {
            Logger.LogInformation("Catalog: {Catalog}", catalogName);
        }

        var operationResult = new OperationResult();
        var blueprint = await _catalogManager.TryGetAsync(blueprintId, operationResult);

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            Logger.LogError("Error looking up blueprint '{BlueprintId}'", blueprintId.FullName);
            operationResult.WriteMessagesToLogger(Logger);
            return;
        }

        if (blueprint == null)
        {
            Logger.LogWarning("Blueprint '{BlueprintId}' not found in any catalog", blueprintId.FullName);
            return;
        }

        Logger.LogInformation("Found blueprint '{BlueprintId}'", blueprintId.FullName);
        Logger.LogInformation("  Description: {Description}", blueprint.Description ?? "(none)");
        Logger.LogInformation("  CK Dependencies: {Count}", blueprint.CkModelDependencies?.Count ?? 0);

        if (!string.IsNullOrEmpty(blueprint.SeedDataPath))
        {
            Logger.LogInformation("  Seed Data: {SeedDataPath}", blueprint.SeedDataPath);
        }

        if (CommandArgumentValue.IsArgumentUsed(_outputArg))
        {
            var outputPath = CommandArgumentValue.GetArgumentScalarValue<string>(_outputArg);
#pragma warning disable CS0618 // GetBlueprintPathAsync is intentional here: this CLI's job is to
            // hand the user an on-disk folder to copy, which only file-system catalogs can
            // produce. Embedded catalogs don't expose a directory and would throw correctly.
            var sourcePath = await _catalogManager.GetBlueprintPathAsync(blueprintId);
#pragma warning restore CS0618

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var targetDir = Path.Combine(outputPath, blueprintId.FullName);
            CopyDirectory(sourcePath, targetDir);

            Logger.LogInformation("Blueprint copied to: {OutputPath}", targetDir);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(targetDir, fileName), true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(subDir);
            CopyDirectory(subDir, Path.Combine(targetDir, dirName));
        }
    }
}
