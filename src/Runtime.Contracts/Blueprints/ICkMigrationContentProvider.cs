using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Provides migration content for CK models.
/// This abstraction allows migrations to be loaded from different sources
/// such as file system, embedded resources, or remote storage.
/// </summary>
public interface ICkMigrationContentProvider
{
    /// <summary>
    /// Gets all available migrations for a specific CK model.
    /// </summary>
    /// <param name="ckModelId">The CK model ID to get migrations for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of parsed migration scripts, ordered by source version</returns>
    Task<IReadOnlyList<CkMigrationScriptDto>> GetMigrationsAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the migration metadata for a specific CK model.
    /// </summary>
    /// <param name="ckModelId">The CK model ID to get metadata for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration metadata or null if not found</returns>
    Task<CkMigrationMetaDto?> GetMigrationMetaAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific migration script by source and target version.
    /// </summary>
    /// <param name="ckModelId">The CK model ID</param>
    /// <param name="sourceVersion">Source version of the migration</param>
    /// <param name="targetVersion">Target version of the migration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration script or null if not found</returns>
    Task<CkMigrationScriptDto?> GetMigrationAsync(
        CkModelId ckModelId,
        string sourceVersion,
        string targetVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if migrations are available for a specific CK model.
    /// </summary>
    /// <param name="ckModelId">The CK model ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if migrations exist for the model</returns>
    Task<bool> HasMigrationsAsync(
        CkModelId ckModelId,
        CancellationToken cancellationToken = default);
}
