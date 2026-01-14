namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Result of a blueprint list operation
/// </summary>
public class BlueprintListResult
{
    /// <summary>
    /// Creates a new instance of <see cref="BlueprintListResult"/>
    /// </summary>
    public BlueprintListResult()
    {
        Items = [];
    }

    /// <summary>
    /// The list of blueprints found
    /// </summary>
    public List<BlueprintCatalogResultItem> Items { get; init; }

    /// <summary>
    /// The total count of blueprints in the catalog
    /// </summary>
    public int TotalCount { get; init; }
}
