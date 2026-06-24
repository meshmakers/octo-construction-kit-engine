using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
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

    /// <summary>
    /// Only the dry-run preview reads the (cache / GitHub-Pages-backed) catalog listing, so it is the only
    /// path that benefits from a pre-read refresh. A forced removal goes straight to the catalog's
    /// authoritative store, so the refresh would be wasted network I/O on the common, destructive path.
    /// </summary>
    public override Task PreValidate()
    {
        return CommandArgumentValue.IsArgumentUsed(_forceArg) ? Task.CompletedTask : base.PreValidate();
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

        // Normalize the requested version once so the dry-run preview and the forced removal agree. CkVersion
        // canonicalises to three components (e.g. "1.2" -> "1.2.0"), and the catalog listing / ids are always
        // canonical; comparing the raw argument would make the dry run miss a version that --force would still
        // delete — the worst direction for a safety preview.
        string? normalizedVersion = null;
        if (!wholeBlueprint)
        {
            try
            {
                normalizedVersion = new CkVersion(version!).ToString();
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException or OverflowException)
            {
                Logger.LogError("Invalid version '{Version}'. Expected 'Major.Minor.Revision' (e.g. 1.2.3)", version);
                return;
            }
        }

        Logger.LogInformation("Unpublishing from catalog '{Catalog}'", catalogName);

        if (!isForced)
        {
            // Dry run: preview from the catalog listing. GitHub catalogs serve that index from GitHub
            // Pages, which can lag a just-published blueprint by up to a minute — so this preview is
            // best-effort, and the forced path below deliberately does NOT depend on it.
            var listResult = await CatalogManager.ListAsync(skip: 0, take: 10000);
            var targets = listResult.Items
                .Where(i => i.CatalogName == catalogName && i.BlueprintId.Name == blueprintName)
                .Where(i => wholeBlueprint || i.BlueprintId.Version.ToString() == normalizedVersion)
                .Select(i => i.BlueprintId)
                .OrderBy(b => b)
                .ToList();

            if (targets.Count == 0)
            {
                Logger.LogInformation(
                    "Dry run — nothing matches '{Name}{Version}' in catalog '{Catalog}'. " +
                    "(A just-published blueprint can take a moment to appear in the index.) " +
                    "Re-run with --force to remove it regardless of the index state",
                    blueprintName, wholeBlueprint ? "" : $"-{normalizedVersion}", catalogName);
                return;
            }

            Logger.LogWarning(
                "Dry run — the following would be PERMANENTLY removed from catalog '{Catalog}'. Re-run with --force to apply:",
                catalogName);
            foreach (var target in targets)
            {
                Logger.LogWarning("  {BlueprintId}", target.FullName);
            }

            return;
        }

        // Forced removal goes directly to the catalog's authoritative store. It is idempotent (a missing
        // blueprint / version is a no-op) and does not consult the read-side index, so it is correct even
        // immediately after a publish — before the index / cache has propagated.
        if (wholeBlueprint)
        {
            await CatalogManager.UnpublishAllVersionsAsync(catalogName, blueprintName);
            Logger.LogInformation("Unpublished all versions of blueprint '{Name}' from catalog '{Catalog}'",
                blueprintName, catalogName);
        }
        else
        {
            await CatalogManager.UnpublishAsync(catalogName, new BlueprintId(blueprintName, normalizedVersion!));
            Logger.LogInformation("Unpublished blueprint '{Name}-{Version}' from catalog '{Catalog}'",
                blueprintName, normalizedVersion, catalogName);
        }
    }
}
