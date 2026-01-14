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
}
