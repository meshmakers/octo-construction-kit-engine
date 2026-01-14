namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
/// Defines options for blueprint catalogs
/// </summary>
public class BlueprintCatalogOptions(string cacheFileName)
{
    /// <summary>
    /// Gets or sets the cache file name for the blueprint catalog
    /// </summary>
    public string CacheFileName { get; set; } = cacheFileName;

    /// <summary>
    /// Gets or sets the cache directory for blueprint catalogs
    /// </summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".octo/blueprint-catalog/cache");
}
