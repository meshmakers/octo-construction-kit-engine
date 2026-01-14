using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Information about available blueprint updates for a tenant
/// </summary>
public class BlueprintUpdateInfo
{
    /// <summary>
    /// Currently applied blueprint version
    /// </summary>
    public required BlueprintId CurrentVersion { get; set; }

    /// <summary>
    /// List of available versions that can be updated to
    /// </summary>
    public required List<BlueprintId> AvailableVersions { get; set; }

    /// <summary>
    /// Recommended version to update to (typically the latest stable)
    /// </summary>
    public BlueprintId? RecommendedVersion { get; set; }

    /// <summary>
    /// Whether a direct migration path exists from current to recommended version
    /// </summary>
    public bool HasMigrationPath { get; set; }

    /// <summary>
    /// Available migration paths with their source versions
    /// </summary>
    public List<string>? AvailableMigrations { get; set; }
}

/// <summary>
/// Preview of changes that would be made by a blueprint update
/// </summary>
public class BlueprintUpdatePreview
{
    /// <summary>
    /// Number of entities that would be added
    /// </summary>
    public int EntitiesToAdd { get; set; }

    /// <summary>
    /// Number of entities that would be updated
    /// </summary>
    public int EntitiesToUpdate { get; set; }

    /// <summary>
    /// Number of entities that would be deleted
    /// </summary>
    public int EntitiesToDelete { get; set; }

    /// <summary>
    /// Detected conflicts that need resolution
    /// </summary>
    public List<BlueprintUpdateConflict> Conflicts { get; set; } = [];

    /// <summary>
    /// Warnings about potential issues
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Whether the update can proceed without manual intervention
    /// </summary>
    public bool CanProceed => Conflicts.Count == 0;
}

/// <summary>
/// A conflict detected during blueprint update preview
/// </summary>
public class BlueprintUpdateConflict
{
    /// <summary>
    /// ID of the entity with the conflict
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// Well-known name of the entity (if available)
    /// </summary>
    public string? EntityWellKnownName { get; set; }

    /// <summary>
    /// Type of the entity
    /// </summary>
    public string? EntityCkTypeId { get; set; }

    /// <summary>
    /// Description of the conflict
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Type of conflict
    /// </summary>
    public ConflictType ConflictType { get; set; }

    /// <summary>
    /// Suggested resolution for this conflict
    /// </summary>
    public ConflictResolution SuggestedResolution { get; set; }
}

/// <summary>
/// Types of conflicts that can occur during updates
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// User has modified a blueprint-managed entity
    /// </summary>
    UserModified,

    /// <summary>
    /// Entity exists but with different type
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// Entity would be deleted but has user modifications
    /// </summary>
    DeleteModified,

    /// <summary>
    /// Required dependency is missing
    /// </summary>
    MissingDependency
}

/// <summary>
/// How to resolve a conflict
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Keep the user's version, skip blueprint changes
    /// </summary>
    KeepUser,

    /// <summary>
    /// Use the blueprint version, overwrite user changes
    /// </summary>
    KeepBlueprint,

    /// <summary>
    /// Attempt to merge changes
    /// </summary>
    Merge,

    /// <summary>
    /// Skip this entity entirely
    /// </summary>
    Skip
}

/// <summary>
/// Options for blueprint update
/// </summary>
public class BlueprintUpdateOptions
{
    /// <summary>
    /// If true, only simulate the update without making changes
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// If true, create a backup before applying changes
    /// </summary>
    public bool CreateBackup { get; set; } = true;

    /// <summary>
    /// Manual conflict resolutions (entity ID -> resolution)
    /// </summary>
    public Dictionary<string, ConflictResolution>? ConflictResolutions { get; set; }

    /// <summary>
    /// If true, continue on non-fatal errors
    /// </summary>
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
/// Result of a blueprint update operation
/// </summary>
public class BlueprintUpdateResult
{
    /// <summary>
    /// Whether the update completed successfully
    /// </summary>
    public bool Success { get; set; }

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
    /// Number of entities skipped due to conflicts
    /// </summary>
    public int EntitiesSkipped { get; set; }

    /// <summary>
    /// Errors that occurred during the update
    /// </summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Warnings generated during the update
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// ID of the backup created (if CreateBackup was true)
    /// </summary>
    public string? BackupId { get; set; }

    /// <summary>
    /// The new blueprint info after update
    /// </summary>
    public TenantBlueprintInfo? NewBlueprintInfo { get; set; }
}
