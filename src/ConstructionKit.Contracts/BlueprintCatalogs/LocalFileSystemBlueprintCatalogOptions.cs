namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
///     Options for the local file system blueprint catalog
/// </summary>
public class LocalFileSystemBlueprintCatalogOptions : BlueprintCatalogOptions
{
    /// <summary>
    ///     Creates a new instance of <see cref="LocalFileSystemBlueprintCatalogOptions" />
    /// </summary>
    public LocalFileSystemBlueprintCatalogOptions() : base("local-blueprint-catalog-cache.json")
    {
        RootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".octo/local-blueprint-catalog");
    }

    /// <summary>
    ///     The local path where the blueprints are stored
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// When true, the local blueprint catalog in the file system is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    ///     Relocates the catalog to the given root path. The catalog cache always moves together with the
    ///     catalog content — a cache pointing at a different root than the content would make one component
    ///     read blueprints that another component never wrote (split-brain). Empty or whitespace values are
    ///     ignored and the defaults remain in place.
    /// </summary>
    /// <param name="rootPath">Root path of the blueprint catalog, e.g. derived from the octo developer shell ROOTPATH</param>
    public void ApplyRootPath(string? rootPath)
    {
        if (rootPath == null || string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        RootPath = rootPath;
        CacheDirectory = Path.Combine(rootPath, "cache");
    }
}
