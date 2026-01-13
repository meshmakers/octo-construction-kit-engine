using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Service for automatically checking and executing CK model migrations when a new version is loaded
/// </summary>
public interface ICkModelUpgradeService
{
    /// <summary>
    /// Checks if a tenant needs migration for any of its loaded CK models and executes them
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="newModelIds">The new CK model versions being loaded (can be version ranges)</param>
    /// <param name="options">Migration execution options</param>
    /// <param name="previouslyInstalledVersions">
    /// Optional dictionary of previously installed versions (model name -> version string).
    /// This is used as a fallback when no MigrationHistory exists for a model.
    /// Should be obtained by calling GetSchemaVersionsAsync BEFORE importing the new CK model.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the upgrade check and migration execution</returns>
    Task<CkModelUpgradeResult> UpgradeModelsAsync(
        string tenantId,
        IEnumerable<CkModelIdVersionRange> newModelIds,
        CkMigrationOptions? options = null,
        IReadOnlyDictionary<string, string>? previouslyInstalledVersions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tenant needs migration for a specific CK model
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="ckModelName">Name of the CK model to check</param>
    /// <param name="targetVersion">Target version being loaded</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Information about whether migration is needed</returns>
    Task<CkModelUpgradeInfo> CheckUpgradeNeededAsync(
        string tenantId,
        string ckModelName,
        string targetVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently tracked CK model versions for a tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of model names to their installed versions</returns>
    Task<IReadOnlyDictionary<string, string>> GetInstalledVersionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a CK model version has been installed for a tenant (without migration)
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="modelId">The exact CK model version that was installed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordInstalledVersionAsync(
        string tenantId,
        CkModelId modelId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of checking and executing CK model upgrades
/// </summary>
public class CkModelUpgradeResult
{
    /// <summary>
    /// Whether all upgrades completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// List of models that were upgraded
    /// </summary>
    public List<CkModelUpgradeInfo> UpgradedModels { get; set; } = [];

    /// <summary>
    /// List of models that were skipped (no migration needed)
    /// </summary>
    public List<CkModelUpgradeInfo> SkippedModels { get; set; } = [];

    /// <summary>
    /// List of models that failed to upgrade
    /// </summary>
    public List<CkModelUpgradeInfo> FailedModels { get; set; } = [];

    /// <summary>
    /// Total entities affected across all migrations
    /// </summary>
    public int TotalEntitiesAffected { get; set; }

    /// <summary>
    /// Errors that occurred during upgrade
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings generated during upgrade
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Creates a successful result with no upgrades needed
    /// </summary>
    public static CkModelUpgradeResult NoUpgradeNeeded() => new()
    {
        Success = true
    };
}

/// <summary>
/// Information about a CK model upgrade
/// </summary>
public class CkModelUpgradeInfo
{
    /// <summary>
    /// Name of the CK model
    /// </summary>
    public required string CkModelName { get; set; }

    /// <summary>
    /// Currently installed version (null if not installed)
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// Target version being loaded
    /// </summary>
    public required string TargetVersion { get; set; }

    /// <summary>
    /// Whether an upgrade is needed
    /// </summary>
    public bool UpgradeNeeded { get; set; }

    /// <summary>
    /// Whether a migration path exists
    /// </summary>
    public bool MigrationPathAvailable { get; set; }

    /// <summary>
    /// Whether the migration contains breaking changes
    /// </summary>
    public bool HasBreakingChanges { get; set; }

    /// <summary>
    /// Result of the migration execution (if executed)
    /// </summary>
    public CkMigrationResult? MigrationResult { get; set; }

    /// <summary>
    /// Error message if the upgrade failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}
