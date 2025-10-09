namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
/// Defines options for model catalogs
/// </summary>
public class CatalogOptions(string cacheFileName)
{
    /// <summary>
    /// Get or sets the cache file name for GitHub catalog
    /// </summary>
    public string CacheFileName { get; set; } = cacheFileName;

    /// <summary>
    /// Gets or sets the cache directory for model catalogs
    /// </summary>
    public string CacheDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".octo/ck-catalog/cache");
}