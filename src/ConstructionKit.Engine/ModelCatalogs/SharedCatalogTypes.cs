using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Shared index and catalog types used by both GitHub and LocalFileSystem model repositories
/// </summary>
internal static class SharedCatalogTypes
{
    /// <summary>
    /// Represents a catalog of all available models
    /// </summary>
    internal class RootCatalog
    {
        public string Version { get; init; } = "1.0";
        public DateTime UpdatedAt { get; set; }
        public List<RootCatalogEntry> Models { get; set; } = [];
    }

    /// <summary>
    /// Represents an entry in the model catalog
    /// </summary>
    internal class RootCatalogEntry
    {
        public string ModelName { get; init; } = string.Empty;
        public string CatalogPath { get; init; } = string.Empty;
    }

    /// <summary>
    /// Represents the overall index for a model showing all major versions
    /// </summary>
    internal class ModelLibraryCatalog
    {
        public string Version { get; init; } = "1.0";
        public string ModelId { get; init; } = string.Empty;
        public List<ModelLibraryCatalogEntry> MajorVersions { get; init; } = [];
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Represents a major version entry in the model index
    /// </summary>
    internal class ModelLibraryCatalogEntry
    {
        public int MajorVersion { get; init; }
        public string CatalogPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the catalog for a specific major version of a model
    /// </summary>
    internal class ModelLibraryVersionsCatalog
    {
        public string Version { get; init; } = "1.0";
        public string ModelId { get; init; } = string.Empty;
        public int MajorVersion { get; init; }
        public string? LatestVersion { get; set; }
        public string? Description { get; set; }
        public List<ModelLibraryVersionsCatalogEntry> Versions { get; init; } = [];
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Represents a specific version entry in a major version index
    /// </summary>
    internal class ModelLibraryVersionsCatalogEntry
    {
        public string Version { get; init; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }
}