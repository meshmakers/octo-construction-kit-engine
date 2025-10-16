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
}