using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Represents cached catalog types for model repositories
/// </summary>
public class CacheTypes
{
    /// <summary>
    /// Represents a cached catalog of all available models
    /// </summary>
    public class CacheCatalog
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
        /// Gets a dictionary of all models in the catalog, keyed by model id
        /// </summary>
        public Dictionary<string, CacheModelEntry> Models { get; set; } = [];
    }

    /// <summary>
    /// Represents a cached entry for a specific model including all its versions
    /// </summary>
    public class CacheModelEntry
    {
        /// <summary>
        /// Gets the unique identifier for the model
        /// </summary>
        public required string ModelId { get; init; }

        /// <summary>
        /// Gets a list of all available versions for this model
        /// </summary>
        public Dictionary<string, CacheModelVersionEntry> Versions { get; set; } = [];
    }

    /// <summary>
    /// Represents a cached entry for a specific version of a model
    /// </summary>
    public class CacheModelVersionEntry
    {
        /// <summary>
        /// Represents the version of the model
        /// </summary>
        public required CkVersion Version { get; init; }

        /// <summary>
        /// Gets or sets an optional description of the model version
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Represents the file path to the cached model file
        /// </summary>
        public required string FilePath { get; init; }
    }

}