using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
/// Command to list available blueprints from catalogs.
/// </summary>
internal class ListCommand : CatalogReadCommand
{
    private readonly IArgument _searchArg;
    private readonly IArgument _catalogArg;

    public ListCommand(
        ILogger<ListCommand> logger,
        IOptions<BpmToolOptions> options,
        IBlueprintCatalogManager catalogManager)
        : base(logger, "list", "Lists available blueprints from catalogs", options, catalogManager)
    {
        _searchArg = CommandArgumentValue.AddArgument("s", "search",
            ["Search term to filter blueprints"], false, 1);

        _catalogArg = CommandArgumentValue.AddArgument("c", "catalog",
            ["Filter by catalog name"], false, 1);
    }

    public override async Task Execute()
    {
        Logger.LogInformation("Listing blueprints");

        var searchTerm = CommandArgumentValue.IsArgumentUsed(_searchArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_searchArg)
            : null;

        var catalogFilter = CommandArgumentValue.IsArgumentUsed(_catalogArg)
            ? CommandArgumentValue.GetArgumentScalarValueOrDefault<string>(_catalogArg)
            : null;

        if (!string.IsNullOrEmpty(searchTerm))
        {
            Logger.LogInformation("Search term: {SearchTerm}", searchTerm);
        }

        if (!string.IsNullOrEmpty(catalogFilter))
        {
            Logger.LogInformation("Catalog filter: {CatalogFilter}", catalogFilter);
        }

        // Get all blueprints (using a large take value to get all)
        var result = await CatalogManager.ListAsync(skip: 0, take: 10000);

        var blueprintCount = 0;
        var currentCatalog = "";

        foreach (var item in result.Items)
        {
            // Apply catalog filter
            if (!string.IsNullOrEmpty(catalogFilter) &&
                !item.CatalogName.Contains(catalogFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(searchTerm))
            {
                var matchesName = item.BlueprintId.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
                var matchesDescription = !string.IsNullOrEmpty(item.Description) &&
                                         item.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);

                if (!matchesName && !matchesDescription)
                {
                    continue;
                }
            }

            // Print catalog header if changed
            if (item.CatalogName != currentCatalog)
            {
                currentCatalog = item.CatalogName;
                Logger.LogInformation("");
                Logger.LogInformation("Catalog: {CatalogName}", currentCatalog);
                Logger.LogInformation("----------------------------------------");
            }

            // Print blueprint info
            if (!string.IsNullOrEmpty(item.Description))
            {
                Logger.LogInformation("  {BlueprintId} - {Description}",
                    item.BlueprintId.FullName, item.Description);
            }
            else
            {
                Logger.LogInformation("  {BlueprintId}", item.BlueprintId.FullName);
            }

            blueprintCount++;
        }

        Logger.LogInformation("");
        Logger.LogInformation("Found {Count} blueprint(s)", blueprintCount);
    }
}
