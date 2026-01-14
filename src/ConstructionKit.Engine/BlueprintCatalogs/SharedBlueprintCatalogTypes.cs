namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Shared index and catalog types used by GitHub blueprint repositories
/// </summary>
internal static class SharedBlueprintCatalogTypes
{
    /// <summary>
    /// Represents a catalog of all available blueprints
    /// </summary>
    internal class RootCatalog
    {
        public string Version { get; init; } = "1.0";
        public DateTime UpdatedAt { get; set; }
        public List<RootCatalogEntry> Blueprints { get; set; } = [];
    }

    /// <summary>
    /// Represents an entry in the blueprint catalog
    /// </summary>
    internal class RootCatalogEntry
    {
        public string BlueprintName { get; init; } = string.Empty;
        public string CatalogPath { get; init; } = string.Empty;
    }

    /// <summary>
    /// Represents the overall index for a blueprint showing all major versions
    /// </summary>
    internal class BlueprintLibraryCatalog
    {
        public string Version { get; init; } = "1.0";
        public string BlueprintId { get; init; } = string.Empty;
        public List<BlueprintLibraryCatalogEntry> MajorVersions { get; init; } = [];
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Represents a major version entry in the blueprint index
    /// </summary>
    internal class BlueprintLibraryCatalogEntry
    {
        public int MajorVersion { get; init; }
        public string CatalogPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the catalog for a specific major version of a blueprint
    /// </summary>
    internal class BlueprintLibraryVersionsCatalog
    {
        public string Version { get; init; } = "1.0";
        public string BlueprintId { get; init; } = string.Empty;
        public int MajorVersion { get; init; }
        public string? LatestVersion { get; set; }
        public string? Description { get; set; }
        public List<BlueprintLibraryVersionsCatalogEntry> Versions { get; init; } = [];
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Represents a specific version entry in a major version index
    /// </summary>
    internal class BlueprintLibraryVersionsCatalogEntry
    {
        public string Version { get; init; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }
}
