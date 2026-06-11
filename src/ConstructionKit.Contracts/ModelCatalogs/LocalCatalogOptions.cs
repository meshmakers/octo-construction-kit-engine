// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     Options for the local file system construction kit catalog
/// </summary>
public class LocalFileSystemCatalogOptions : CatalogOptions
{
    /// <summary>
    ///     Creates a new instance of <see cref="LocalFileSystemCatalogOptions" />
    /// </summary>
    public LocalFileSystemCatalogOptions(): base("local-catalog-cache.json")
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".octo/local-catalog");
    }

    /// <summary>
    ///     The local path where the CK models are stored
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// When true, the local catalog in the file system is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    ///     Relocates the catalog to the given root path. The catalog cache always moves together with the
    ///     catalog content — a cache pointing at a different root than the content would make one component
    ///     read models that another component never wrote (split-brain). Empty or whitespace values are
    ///     ignored and the defaults remain in place.
    /// </summary>
    /// <param name="rootPath">Root path of the catalog, e.g. from the OctoLocalCatalogRootPath MSBuild property</param>
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