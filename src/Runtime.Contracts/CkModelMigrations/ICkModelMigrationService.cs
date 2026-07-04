using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;

/// <summary>
/// Service for executing CK model migrations when upgrading between versions
/// </summary>
public interface ICkModelMigrationService
{
    /// <summary>
    /// Executes a migration from one CK model version to another for a specific tenant
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="fromModel">Source CK model version</param>
    /// <param name="toModel">Target CK model version</param>
    /// <param name="options">Migration execution options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the migration execution</returns>
    Task<CkMigrationResult> MigrateAsync(
        string tenantId,
        CkModelId fromModel,
        CkModelId toModel,
        CkMigrationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a migration path between two CK model versions
    /// </summary>
    /// <param name="fromModel">Source CK model version</param>
    /// <param name="toModel">Target CK model version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration path if available, null otherwise</returns>
    Task<CkMigrationPath?> FindMigrationPathAsync(
        CkModelId fromModel,
        CkModelId toModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current migration status for a CK model in a specific tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ckModelName">Name of the CK model (e.g., "System")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current migration status</returns>
    Task<CkMigrationStatus> GetStatusAsync(
        string tenantId,
        string ckModelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the migration history for a CK model in a specific tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ckModelName">Name of the CK model (e.g., "System")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of executed migrations</returns>
    Task<IReadOnlyList<CkMigrationHistoryEntry>> GetHistoryAsync(
        string tenantId,
        string ckModelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a migration without executing it
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="fromModel">Source CK model version</param>
    /// <param name="toModel">Target CK model version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<CkMigrationValidationResult> ValidateAsync(
        string tenantId,
        CkModelId fromModel,
        CkModelId toModel,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for CK model migration execution
/// </summary>
public class CkMigrationOptions
{
    /// <summary>
    /// If true, only simulate the migration without making changes
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// If true, continue executing steps even if some fail
    /// </summary>
    public bool ContinueOnError { get; set; } = false;

    /// <summary>
    /// If true, also migrate entities not created by blueprints (user-created entities)
    /// </summary>
    public bool IncludeUserEntities { get; set; } = true;
}

/// <summary>
/// Result of a CK model migration execution
/// </summary>
public class CkMigrationResult
{
    /// <summary>
    /// Whether the migration completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Source CK model version
    /// </summary>
    public required CkModelId FromModel { get; set; }

    /// <summary>
    /// Target CK model version
    /// </summary>
    public required CkModelId ToModel { get; set; }

    /// <summary>
    /// Number of entities added
    /// </summary>
    public int EntitiesAdded { get; set; }

    /// <summary>
    /// Number of entities updated
    /// </summary>
    public int EntitiesUpdated { get; set; }

    /// <summary>
    /// Number of entities deleted
    /// </summary>
    public int EntitiesDeleted { get; set; }

    /// <summary>
    /// Total number of entities affected
    /// </summary>
    public int TotalEntitiesAffected => EntitiesAdded + EntitiesUpdated + EntitiesDeleted;

    /// <summary>
    /// Duration of the migration in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Errors that occurred during migration
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings generated during migration
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static CkMigrationResult Succeeded(CkModelId fromModel, CkModelId toModel) => new()
    {
        Success = true,
        FromModel = fromModel,
        ToModel = toModel
    };

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static CkMigrationResult Failed(CkModelId fromModel, CkModelId toModel, string error) => new()
    {
        Success = false,
        FromModel = fromModel,
        ToModel = toModel,
        Errors = [error]
    };
}

/// <summary>
/// Represents a migration path between two CK model versions
/// </summary>
public class CkMigrationPath
{
    /// <summary>
    /// Source CK model version
    /// </summary>
    public required CkModelId FromModel { get; set; }

    /// <summary>
    /// Target CK model version
    /// </summary>
    public required CkModelId ToModel { get; set; }

    /// <summary>
    /// Ordered list of migration steps to execute
    /// </summary>
    public List<CkMigrationStep> Steps { get; set; } = [];

    /// <summary>
    /// Whether this migration contains breaking changes
    /// </summary>
    public bool HasBreakingChanges { get; set; }

    /// <summary>
    /// Description of the migration
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Indicates whether this is a partial migration path.
    /// A partial path occurs when data migrations are defined up to an intermediate version
    /// (e.g., 1.0.3 -> 2.0.0) but the actual target version is higher (e.g., 2.0.2).
    /// The remaining version jump (2.0.0 -> 2.0.2) is schema-only and doesn't need data migration.
    /// </summary>
    public bool IsPartialPath { get; set; }
}

/// <summary>
/// A single step in a CK migration path (for multi-hop migrations)
/// </summary>
public class CkMigrationStep
{
    /// <summary>
    /// Source version for this step
    /// </summary>
    public required string FromVersion { get; set; }

    /// <summary>
    /// Target version for this step
    /// </summary>
    public required string ToVersion { get; set; }

    /// <summary>
    /// The parsed migration script. Either this or ScriptPath must be provided.
    /// </summary>
    public CkMigrationScriptDto? Script { get; set; }

    /// <summary>
    /// Path to the migration script (for file-system based loading).
    /// Either this or Script must be provided.
    /// </summary>
    public string? ScriptPath { get; set; }

    /// <summary>
    /// Description of this migration step
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this step contains breaking changes
    /// </summary>
    public bool Breaking { get; set; }
}

/// <summary>
/// Current migration status for a CK model in a tenant
/// </summary>
public class CkMigrationStatus
{
    /// <summary>
    /// Name of the CK model
    /// </summary>
    public required string CkModelName { get; set; }

    /// <summary>
    /// Currently installed version (based on last successful migration)
    /// </summary>
    public string? InstalledVersion { get; set; }

    /// <summary>
    /// Latest available version in the CK catalog
    /// </summary>
    public string? LatestAvailableVersion { get; set; }

    /// <summary>
    /// Whether an update is available
    /// </summary>
    public bool UpdateAvailable => InstalledVersion != null &&
                                   LatestAvailableVersion != null &&
                                   InstalledVersion != LatestAvailableVersion;

    /// <summary>
    /// Whether a migration path exists from installed to latest version
    /// </summary>
    public bool MigrationPathAvailable { get; set; }

    /// <summary>
    /// Last migration execution time
    /// </summary>
    public DateTime? LastMigrationAt { get; set; }

    /// <summary>
    /// Whether last migration was successful
    /// </summary>
    public bool? LastMigrationSuccess { get; set; }
}

/// <summary>
/// Entry in the CK migration history
/// </summary>
public class CkMigrationHistoryEntry
{
    /// <summary>
    /// Name of the CK model that was migrated
    /// </summary>
    public required string CkModelName { get; set; }

    /// <summary>
    /// Source version before migration
    /// </summary>
    public required string FromVersion { get; set; }

    /// <summary>
    /// Target version after migration
    /// </summary>
    public required string ToVersion { get; set; }

    /// <summary>
    /// When the migration was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; }

    /// <summary>
    /// Whether the migration was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of entities affected (sum of added, updated, deleted)
    /// </summary>
    public int EntitiesAffected { get; set; }

    /// <summary>
    /// Number of entities added during migration
    /// </summary>
    public int EntitiesAdded { get; set; }

    /// <summary>
    /// Number of entities updated during migration
    /// </summary>
    public int EntitiesUpdated { get; set; }

    /// <summary>
    /// Number of entities deleted during migration
    /// </summary>
    public int EntitiesDeleted { get; set; }

    /// <summary>
    /// Duration of the migration in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Errors if migration failed
    /// </summary>
    public List<string>? Errors { get; set; }

    /// <summary>
    /// Warning messages generated during migration
    /// </summary>
    public List<string>? Warnings { get; set; }

    /// <summary>
    /// Identifier of the backup created before migration
    /// </summary>
    public string? BackupId { get; set; }
}

/// <summary>
/// Result of CK migration validation
/// </summary>
public class CkMigrationValidationResult
{
    /// <summary>
    /// Whether the migration is valid and can be executed
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Validation errors that would prevent migration
    /// </summary>
    public List<CkMigrationValidationIssue> Errors { get; set; } = [];

    /// <summary>
    /// Validation warnings that don't prevent migration
    /// </summary>
    public List<CkMigrationValidationIssue> Warnings { get; set; } = [];

    /// <summary>
    /// Estimated number of entities that will be affected
    /// </summary>
    public int EstimatedEntitiesAffected { get; set; }

    /// <summary>
    /// Creates a valid result
    /// </summary>
    public static CkMigrationValidationResult Valid() => new() { IsValid = true };

    /// <summary>
    /// Creates an invalid result with an error
    /// </summary>
    public static CkMigrationValidationResult Invalid(string error) => new()
    {
        IsValid = false,
        Errors = [new CkMigrationValidationIssue { Message = error }]
    };
}

/// <summary>
/// A validation issue found in a CK migration
/// </summary>
public class CkMigrationValidationIssue
{
    /// <summary>
    /// The step ID where the issue was found (null for global issues)
    /// </summary>
    public string? StepId { get; set; }

    /// <summary>
    /// Description of the issue
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// The property or element that has the issue
    /// </summary>
    public string? PropertyPath { get; set; }
}
