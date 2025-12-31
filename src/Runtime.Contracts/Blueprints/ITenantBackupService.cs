namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Service for creating and restoring tenant backups before blueprint updates
/// </summary>
public interface ITenantBackupService
{
    /// <summary>
    /// Creates a backup of a tenant's current state
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="reason">Reason for the backup (e.g., "Before update to MyBlueprint-2.0.0")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Backup information including the backup ID</returns>
    Task<BackupInfo> CreateBackupAsync(
        string tenantId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a tenant from a backup
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="backupId">Backup identifier to restore from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the restore operation</returns>
    Task<BackupRestoreResult> RestoreBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available backups for a tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available backups</returns>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific backup by ID
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="backupId">Backup identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Backup information, or null if not found</returns>
    Task<BackupInfo?> GetBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="backupId">Backup identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the backup was deleted, false if not found</returns>
    Task<bool> DeleteBackupAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a tenant backup
/// </summary>
public class BackupInfo
{
    /// <summary>
    /// Unique identifier for the backup
    /// </summary>
    public required string BackupId { get; set; }

    /// <summary>
    /// Tenant that was backed up
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>
    /// When the backup was created
    /// </summary>
    public required DateTime CreatedAt { get; set; }

    /// <summary>
    /// Reason for the backup
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Blueprint version at time of backup (if applicable)
    /// </summary>
    public string? BlueprintVersion { get; set; }

    /// <summary>
    /// Number of entities in the backup
    /// </summary>
    public int EntityCount { get; set; }

    /// <summary>
    /// Size of the backup in bytes (if applicable)
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Type of backup
    /// </summary>
    public BackupType BackupType { get; set; }

    /// <summary>
    /// Path or location where backup is stored
    /// </summary>
    public string? StorageLocation { get; set; }
}

/// <summary>
/// Types of backups
/// </summary>
public enum BackupType
{
    /// <summary>
    /// Full backup of all tenant data
    /// </summary>
    Full,

    /// <summary>
    /// Incremental backup of changes since last backup
    /// </summary>
    Incremental,

    /// <summary>
    /// Snapshot backup for blueprint updates
    /// </summary>
    BlueprintUpdate
}

/// <summary>
/// Result of a backup restore operation
/// </summary>
public class BackupRestoreResult
{
    /// <summary>
    /// Whether the restore was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Number of entities restored
    /// </summary>
    public int EntitiesRestored { get; set; }

    /// <summary>
    /// Errors that occurred during restore
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings generated during restore
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// The backup that was restored
    /// </summary>
    public BackupInfo? RestoredBackup { get; set; }
}
