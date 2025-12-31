namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Result of a blueprint search operation
/// </summary>
public class BlueprintSearchResult
{
    /// <summary>
    /// Creates a new instance of <see cref="BlueprintSearchResult"/>
    /// </summary>
    public BlueprintSearchResult()
    {
        Items = [];
    }

    /// <summary>
    /// The list of blueprints found
    /// </summary>
    public List<BlueprintCatalogResultItem> Items { get; init; }

    /// <summary>
    /// The total count of blueprints matching the search
    /// </summary>
    public int TotalCount { get; init; }
}
