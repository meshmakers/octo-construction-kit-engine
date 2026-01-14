using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Represents cached catalog types for blueprint repositories
/// </summary>
public class BlueprintCacheTypes
{
    /// <summary>
    /// Represents a cached catalog of all available blueprints
    /// </summary>
    public class BlueprintCacheCatalog
    {
        /// <summary>
        /// Gets or sets the version of the catalog format
        /// </summary>
        public string Version { get; init; } = "1.0";

        /// <summary>
        /// Gets or sets the timestamp of the last update of the catalog
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Gets a dictionary of all blueprints in the catalog, keyed by blueprint name
        /// </summary>
        public Dictionary<string, BlueprintCacheEntry> Blueprints { get; set; } = [];
    }

    /// <summary>
    /// Represents a cached entry for a specific blueprint including all its versions
    /// </summary>
    public class BlueprintCacheEntry
    {
        /// <summary>
        /// Gets the unique identifier for the blueprint (name only, without version)
        /// </summary>
        public required string BlueprintName { get; init; }

        /// <summary>
        /// Gets a list of all available versions for this blueprint
        /// </summary>
        public Dictionary<string, BlueprintCacheVersionEntry> Versions { get; set; } = [];
    }

    /// <summary>
    /// Represents a cached entry for a specific version of a blueprint
    /// </summary>
    public class BlueprintCacheVersionEntry
    {
        /// <summary>
        /// Represents the version of the blueprint
        /// </summary>
        public required CkVersion Version { get; init; }

        /// <summary>
        /// Gets or sets an optional description of the blueprint version
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Represents the directory path to the blueprint
        /// </summary>
        public required string DirectoryPath { get; init; }
    }
}
