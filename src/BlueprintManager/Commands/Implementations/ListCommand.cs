using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
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

        var items = await ListAllAsync();

        var blueprintCount = 0;
        var currentCatalog = "";

        foreach (var item in items)
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

    /// <summary>
    /// Reads the complete merged catalog listing by paging until <see cref="BlueprintListResult.TotalCount" />
    /// is reached (AB#5650: a fixed <c>ListAsync(0, 10000)</c> window silently truncated the listing), then
    /// groups the entries by catalog (in catalog order) so every catalog header is printed exactly once.
    /// Within a catalog the manager's order (blueprint name, then semantic version) is kept.
    /// </summary>
    private async Task<IReadOnlyList<BlueprintCatalogResultItem>> ListAllAsync()
    {
        var items = new List<BlueprintCatalogResultItem>();
        while (true)
        {
            var page = await CatalogManager.ListAsync(skip: items.Count, take: PageSize);
            items.AddRange(page.Items);

            // An empty page also ends the loop, so a TotalCount that shrinks between calls cannot spin forever.
            if (page.Items.Count == 0 || items.Count >= page.TotalCount)
            {
                break;
            }
        }

        var catalogOrder = CatalogManager.GetCatalogList()
            .Select((catalog, index) => (catalog.Item1, index))
            .GroupBy(c => c.Item1)
            .ToDictionary(g => g.Key, g => g.First().index);

        // OrderBy is stable: entries of one catalog keep the manager's name/version order.
        return items
            .OrderBy(item => catalogOrder.GetValueOrDefault(item.CatalogName, int.MaxValue))
            .ThenBy(item => item.CatalogName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Page size used to read the merged catalog listing. Not an upper bound: <see cref="ListAllAsync" /> pages
    /// until the reported total count is reached.
    /// </summary>
    internal const int PageSize = 500;
}
