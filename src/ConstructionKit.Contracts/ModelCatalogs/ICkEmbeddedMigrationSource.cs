using System.Reflection;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
/// Interface for embedded CK model migration sources.
/// Implementations provide information about where to find embedded migration resources for a CK model.
/// </summary>
public interface ICkEmbeddedMigrationSource
{
    /// <summary>
    /// Gets the name of the CK model this migration source is for.
    /// </summary>
    string CkModelName { get; }

    /// <summary>
    /// Gets the assembly containing the embedded migration resources.
    /// </summary>
    Assembly Assembly { get; }

    /// <summary>
    /// Gets the base namespace for the migration resources.
    /// Migration files are expected to be at: {ResourceNamespace}.migrations.migration-meta.yaml
    /// </summary>
    string ResourceNamespace { get; }
}
