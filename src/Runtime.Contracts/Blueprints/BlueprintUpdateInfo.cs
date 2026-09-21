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
    /// Number of entities whose stored attributes would actually change. AB#5297: this used to
    /// be the number of blueprint-managed (locked) entities, whatever their content - a target
    /// identical to the tenant reported 137 updates - and the three enabled-flag resets hiding
    /// among them were invisible. Every entity counted here appears in <see cref="Changes" />.
    /// </summary>
    public int EntitiesToUpdate { get; set; }

    /// <summary>
    /// Locked entities the update re-applies without changing a single attribute (the apply
    /// still rewrites them so their blueprint stamp stays consistent). Reported separately so
    /// "137 entities" no longer reads as "137 things will change".
    /// </summary>
    public int EntitiesUnchanged { get; set; }

    /// <summary>
    /// Number of entities that would be deleted
    /// </summary>
    public int EntitiesToDelete { get; set; }

    /// <summary>
    /// Per entity, the attributes the update would set to a different value than the tenant
    /// holds now - old and new value included. Attributes the tenant owns (RuntimeState,
    /// TenantOwned, Secret) are preserved by the apply and therefore never listed. An entry with
    /// a null new value means the seed no longer carries the attribute and the upsert would clear
    /// it. This is the list an operator has to read before applying to production.
    /// </summary>
    public List<BlueprintEntityChange> Changes { get; set; } = [];

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
/// One blueprint-managed entity whose stored attributes the update would change (AB#5297).
/// </summary>
public class BlueprintEntityChange
{
    /// <summary>
    /// Runtime id of the tenant entity.
    /// </summary>
    public required string EntityId { get; set; }

    /// <summary>
    /// Well-known name of the entity, when it has one.
    /// </summary>
    public string? EntityWellKnownName { get; set; }

    /// <summary>
    /// The entity's <c>rtDisplayName</c> as stored on the tenant - what an operator recognises
    /// it by ("Email Import (Microsoft 365)"), where the well-known name is often absent.
    /// </summary>
    public string? EntityDisplayName { get; set; }

    /// <summary>
    /// CK type of the entity.
    /// </summary>
    public required string EntityCkTypeId { get; set; }

    /// <summary>
    /// The attributes that differ, with the value the tenant holds and the value the seed
    /// would write.
    /// </summary>
    public List<BlueprintAttributeChange> Attributes { get; set; } = [];
}

/// <summary>
/// One attribute of a <see cref="BlueprintEntityChange" />: what is stored now and what the
/// seed would write. Values are in transport shape (records as DTOs, enums as their key).
/// </summary>
public class BlueprintAttributeChange
{
    /// <summary>
    /// Attribute name as declared on the CK type.
    /// </summary>
    public required string AttributeName { get; set; }

    /// <summary>
    /// The value stored on the tenant today; null when the tenant has no value.
    /// </summary>
    public object? OldValue { get; set; }

    /// <summary>
    /// The value the seed would write; null when the seed omits the attribute, which the upsert
    /// turns into a cleared value.
    /// </summary>
    public object? NewValue { get; set; }
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
    MissingDependency,

    /// <summary>
    /// An update was requested in <see cref="BlueprintUpdateMode.Migration" /> mode, but the
    /// target blueprint ships no migration script whose <c>fromVersion</c> matches the
    /// version of that blueprint currently installed on the tenant.
    /// </summary>
    MissingMigrationScript
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
    /// Locked entities re-applied without any attribute change (AB#5297). Counted apart from
    /// <see cref="EntitiesUpdated" /> so the result matches the preview's reading.
    /// </summary>
    public int EntitiesUnchanged { get; set; }

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
    /// The new blueprint info after update
    /// </summary>
    public TenantBlueprintInfo? NewBlueprintInfo { get; set; }
}
