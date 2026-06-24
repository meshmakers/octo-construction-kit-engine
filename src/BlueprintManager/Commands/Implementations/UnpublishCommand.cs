using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to remove a blueprint from a catalog. The inverse of <see cref="PublishCommand" />: it removes
/// either a single version (<c>-r</c> given) or every version of a blueprint (<c>-r</c> omitted). Destructive
/// operations are gated behind <c>--force</c>; without it the command performs a dry run.
/// </summary>
internal class UnpublishCommand : CatalogReadCommand
{
    private readonly IArgument _blueprintArg;
    private readonly IArgument _versionArg;
    private readonly IArgument _catalogArg;
    private readonly IArgument _forceArg;

    public UnpublishCommand(
        ILogger<UnpublishCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager)
        : base(logger, "unpublish", "Removes a blueprint (one version or all versions) from a catalog", options, catalogManager)
    {
        _blueprintArg = CommandArgumentValue.AddArgument("b", "blueprint",
            ["Name of the blueprint to remove (e.g. 'MyBlueprint')"], true, 1);

        _versionArg = CommandArgumentValue.AddArgument("r", "version",
            ["Exact version to remove (e.g. '1.2.3'). Omit to remove ALL versions of the blueprint"], false, 1);

        _catalogArg = CommandArgumentValue.AddArgument("c", "catalog",
            [$"Name of the target catalog (default: {LocalFileSystemBlueprintCatalog.Name})"], false, 1);

        _forceArg = CommandArgumentValue.AddArgument("f", "force",
            ["Actually perform the removal. Without this flag the command only prints what would be removed"], false, 0);
    }

    public override async Task Execute()
    {
        var blueprintName = CommandArgumentValue.GetArgumentScalarValue<string>(_blueprintArg);
        var version = CommandArgumentValue.IsArgumentUsed(_versionArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_versionArg)
            : null;
        var catalogName = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg) ?? LocalFileSystemBlueprintCatalog.Name
            : LocalFileSystemBlueprintCatalog.Name;
        var isForced = CommandArgumentValue.IsArgumentUsed(_forceArg);

        var wholeBlueprint = string.IsNullOrWhiteSpace(version);

        Logger.LogInformation("Unpublishing from catalog '{Catalog}'", catalogName);

        // Resolve the actual targets in the catalog so the dry run is accurate and a missing blueprint is a
        // clear no-op. The catalog layer is itself idempotent, so this is for reporting, not correctness.
        var listResult = await CatalogManager.ListAsync(skip: 0, take: 10000);
        var targets = listResult.Items
            .Where(i => i.CatalogName == catalogName && i.BlueprintId.Name == blueprintName)
            .Where(i => wholeBlueprint || i.BlueprintId.Version.ToString() == version)
            .Select(i => i.BlueprintId)
            .OrderBy(b => b)
            .ToList();

        if (targets.Count == 0)
        {
            if (wholeBlueprint)
            {
                Logger.LogInformation(
                    "Blueprint '{Name}' has no published versions in catalog '{Catalog}'. Nothing to unpublish",
                    blueprintName, catalogName);
            }
            else
            {
                Logger.LogInformation(
                    "Blueprint '{Name}-{Version}' is not present in catalog '{Catalog}'. Nothing to unpublish",
                    blueprintName, version, catalogName);
            }

            return;
        }

        if (!isForced)
        {
            Logger.LogWarning(
                "Dry run — the following would be PERMANENTLY removed from catalog '{Catalog}'. Re-run with --force to apply:",
                catalogName);
            foreach (var target in targets)
            {
                Logger.LogWarning("  {BlueprintId}", target.FullName);
            }

            return;
        }

        if (wholeBlueprint)
        {
            await CatalogManager.UnpublishAllVersionsAsync(catalogName, blueprintName);
            Logger.LogInformation("Unpublished all {Count} version(s) of blueprint '{Name}' from catalog '{Catalog}'",
                targets.Count, blueprintName, catalogName);
        }
        else
        {
            await CatalogManager.UnpublishAsync(catalogName, new BlueprintId(blueprintName, version!));
            Logger.LogInformation("Unpublished blueprint '{Name}-{Version}' from catalog '{Catalog}'",
                blueprintName, version, catalogName);
        }
    }
}
