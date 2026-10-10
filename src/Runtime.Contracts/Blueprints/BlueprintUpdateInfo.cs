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
    /// Attributes whose non-empty tenant value the seed would blank (AB#6315, incident AB#6310): the
    /// seed carries an empty value or omits the attribute. Detected by the same engine guard the
    /// update applies (<c>SeedValueGuard</c>). Without confirmation the update keeps the tenant
    /// values (<see cref="BlueprintBlankedAttribute.AppliedOnUpdate" /> is false); to blank them the
    /// caller confirms explicitly via <see cref="BlueprintUpdateOptions.AllowBlanking" /> or
    /// <see cref="BlueprintUpdateOptions.ConfirmedBlankings" />. Never carries values, only summaries.
    /// </summary>
    public List<BlueprintBlankedAttribute> BlankedAttributes { get; set; } = [];

    /// <summary>
    /// AB#6383: tenant-owned seed entities (<c>rtBlueprintLocked: false</c> in the seed) the tenant
    /// still holds. The update does not touch their attributes; it only refreshes their blueprint
    /// stamp. They raise no conflict.
    /// </summary>
    public List<BlueprintTenantOwnedEntity> TenantOwnedSkipped { get; set; } = [];

    /// <summary>
    /// AB#6383: tenant-owned seed entities the tenant no longer holds although the previously
    /// installed version's seed contained them - the tenant deleted them, so the update does not
    /// bring them back.
    /// </summary>
    public List<BlueprintTenantOwnedEntity> TenantOwnedStaysDeleted { get; set; } = [];

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
    /// would write. Empty when <see cref="Note" /> explains why no comparison was possible.
    /// </summary>
    public List<BlueprintAttributeChange> Attributes { get; set; } = [];

    /// <summary>
    /// Set when the entity is counted as an update without an attribute list - the CK type was
    /// not available to compare against, so the preview reports it rather than hides it. Every
    /// entity counted in <see cref="BlueprintUpdatePreview.EntitiesToUpdate" /> is listed, one
    /// way or the other.
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// A tenant-owned seed entity (AB#6383) an update did not write: either the tenant still holds it
/// (<see cref="BlueprintUpdatePreview.TenantOwnedSkipped" />) or deleted it
/// (<see cref="BlueprintUpdatePreview.TenantOwnedStaysDeleted" />).
/// </summary>
public class BlueprintTenantOwnedEntity
{
    /// <summary>
    /// Identity key of the seed entity: its <c>rtWellKnownName</c>, else its <c>rtId</c>.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// CK type of the entity.
    /// </summary>
    public required string CkTypeId { get; set; }

    /// <summary>
    /// Runtime id of the tenant entity; null when the tenant no longer holds it.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Well-known name of the entity, when it has one.
    /// </summary>
    public string? WellKnownName { get; set; }
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
/// One attribute whose non-empty tenant value a blueprint update would blank (AB#6315). Values are
/// described, never included: the attribute may hold credentials.
/// </summary>
public class BlueprintBlankedAttribute
{
    /// <summary>
    /// Runtime id of the tenant entity.
    /// </summary>
    public required string RtId { get; set; }

    /// <summary>
    /// CK type of the entity.
    /// </summary>
    public required string CkTypeId { get; set; }

    /// <summary>
    /// Attribute name as stored.
    /// </summary>
    public required string AttributeName { get; set; }

    /// <summary>
    /// Why the seed counts as blanking: <c>SeedEmpty</c> (the seed declares an empty value, or a JSON
    /// text that empties a string the tenant filled) or <c>SeedOmitted</c> (the seed does not declare
    /// the attribute).
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Value-free description of what the tenant holds, e.g. <c>string (223 chars)</c>.
    /// </summary>
    public string? CurrentSummary { get; set; }

    /// <summary>
    /// Value-free description of what the seed carries, e.g. <c>string (110 chars)</c>, <c>empty string</c>
    /// or <c>omitted</c>.
    /// </summary>
    public string? IncomingSummary { get; set; }

    /// <summary>
    /// <c>false</c>: the update keeps the tenant value (default). <c>true</c>: the update blanks it
    /// (on a result: it did; on a preview: never, as a preview applies nothing).
    /// </summary>
    public bool AppliedOnUpdate { get; set; }
}

/// <summary>
/// An operator's explicit confirmation that one attribute of one entity may be blanked by the
/// update (AB#6315). Take the values from <see cref="BlueprintUpdatePreview.BlankedAttributes" />.
/// </summary>
public class BlueprintBlankingConfirmation
{
    /// <summary>
    /// Runtime id of the entity.
    /// </summary>
    public required string RtId { get; set; }

    /// <summary>
    /// Attribute name (case-insensitive).
    /// </summary>
    public required string AttributeName { get; set; }
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

    /// <summary>
    /// AB#6315: explicit confirmation that the update may blank EVERY attribute listed in
    /// <see cref="BlueprintUpdatePreview.BlankedAttributes" /> (engine policy
    /// <c>RtImportBlankingPolicy.Allow</c>). Default <c>false</c>: tenant values are kept and the
    /// result lists them. Prefer <see cref="ConfirmedBlankings" /> to confirm single attributes.
    /// </summary>
    public bool AllowBlanking { get; set; } = false;

    /// <summary>
    /// AB#6315: confirms blanking for exactly these entity/attribute pairs; everything else listed
    /// stays kept. Ignored when <see cref="AllowBlanking" /> is true.
    /// </summary>
    public List<BlueprintBlankingConfirmation>? ConfirmedBlankings { get; set; }
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
    /// AB#6315: the attributes the seed would have blanked, with what happened to each
    /// (<see cref="BlueprintBlankedAttribute.AppliedOnUpdate" />). Empty when nothing was blanked.
    /// Nothing is silent: a kept tenant value is listed here as well.
    /// </summary>
    public List<BlueprintBlankedAttribute> BlankedAttributes { get; set; } = [];

    /// <summary>
    /// AB#6383: tenant-owned seed entities the tenant holds; left untouched apart from their stamp.
    /// Counted in <see cref="EntitiesSkipped" />.
    /// </summary>
    public List<BlueprintTenantOwnedEntity> TenantOwnedSkipped { get; set; } = [];

    /// <summary>
    /// AB#6383: tenant-owned seed entities the tenant deleted; not re-created. Counted in
    /// <see cref="EntitiesSkipped" />.
    /// </summary>
    public List<BlueprintTenantOwnedEntity> TenantOwnedStaysDeleted { get; set; } = [];

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
