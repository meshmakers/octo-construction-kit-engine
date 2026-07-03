namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Per-catalog result of a blueprint catalog cache refresh
/// </summary>
public class BlueprintCatalogRefreshResult
{
    /// <summary>
    /// Name of the catalog
    /// </summary>
    public required string CatalogName { get; init; }

    /// <summary>
    /// Outcome of the refresh for this catalog
    /// </summary>
    public BlueprintCatalogRefreshStatus Status { get; init; }

    /// <summary>
    /// Optional detail message (failure reason for <see cref="BlueprintCatalogRefreshStatus.Failed"/>,
    /// skip reason for <see cref="BlueprintCatalogRefreshStatus.Skipped"/>)
    /// </summary>
    public string? Message { get; init; }
}
