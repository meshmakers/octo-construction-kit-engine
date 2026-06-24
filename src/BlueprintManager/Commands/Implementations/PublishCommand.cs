using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to publish a blueprint to a catalog.
/// </summary>
internal class PublishCommand : Command<BpmToolOptions>
{
    private readonly IBlueprintCatalogManager _catalogManager;
    private readonly IBlueprintCompilerService _blueprintCompilerService;
    private readonly IArgument _pathArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _forceArg;

    public PublishCommand(
        ILogger<PublishCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager,
        IBlueprintCompilerService blueprintCompilerService)
        : base(logger, "publish", "Publishes a blueprint to a catalog", options)
    {
        _catalogManager = catalogManager;
        _blueprintCompilerService = blueprintCompilerService;

        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Path to the blueprint directory"], true, 1);

        _catalogArg = CommandArgumentValue.AddArgument("c", "catalog",
            [$"Name of the target catalog (default: {LocalFileSystemBlueprintCatalog.Name})"], false, 1);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Replace existing blueprint if it exists"], false, 0);
    }

    public override async Task Execute()
    {
        var path = CommandArgumentValue.GetArgumentScalarValue<string>(_pathArg);
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg) ?? LocalFileSystemBlueprintCatalog.Name
            : LocalFileSystemBlueprintCatalog.Name;
        var isForced = CommandArgumentValue.IsArgumentUsed(_forceArg);

        Logger.LogInformation("Publishing blueprint");
        Logger.LogInformation("Path: {Path}", path);
        Logger.LogInformation("Catalog: {Catalog}", catalogName);

        if (isForced)
        {
            Logger.LogInformation("Force mode: existing blueprints will be replaced");
        }

        // First validate the blueprint
        var operationResult = new OperationResult();
        var blueprintMeta = await _blueprintCompilerService.ValidateAsync(path, operationResult);

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            Logger.LogError("Blueprint validation failed");
            operationResult.WriteMessagesToLogger(Logger);
            return;
        }

        Logger.LogInformation("Blueprint '{BlueprintId}' validated successfully", blueprintMeta.BlueprintId.FullName);

        // Check if blueprint already exists
        var exists = await _catalogManager.IsExistingAsync(blueprintMeta.BlueprintId);

        if (exists && !isForced)
        {
            Logger.LogError("Blueprint '{BlueprintId}' already exists in catalog. Use --force to replace",
                blueprintMeta.BlueprintId.FullName);
            return;
        }

        if (exists)
        {
            Logger.LogWarning("Replacing existing blueprint '{BlueprintId}'", blueprintMeta.BlueprintId.FullName);
        }

        // Publish the blueprint
        await _catalogManager.PublishAsync(catalogName, blueprintMeta, path, isForced);

        Logger.LogInformation("Blueprint '{BlueprintId}' published successfully to catalog '{Catalog}'",
            blueprintMeta.BlueprintId.FullName, catalogName);
    }
}
