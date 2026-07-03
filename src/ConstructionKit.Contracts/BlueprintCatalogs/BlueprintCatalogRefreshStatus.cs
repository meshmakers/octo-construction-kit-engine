namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Outcome of refreshing a single blueprint catalog cache
/// </summary>
public enum BlueprintCatalogRefreshStatus
{
    /// <summary>
    /// The catalog cache was rebuilt successfully (a no-op refresh, e.g. of the embedded
    /// resource catalog, also counts as refreshed)
    /// </summary>
    Refreshed,

    /// <summary>
    /// The catalog was skipped because it is not readable or does not support the
    /// requested source identifier
    /// </summary>
    Skipped,

    /// <summary>
    /// The catalog refresh threw an exception; see <see cref="BlueprintCatalogRefreshResult.Message"/>
    /// </summary>
    Failed
}
