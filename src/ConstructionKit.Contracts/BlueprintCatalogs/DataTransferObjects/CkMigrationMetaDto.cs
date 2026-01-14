using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
/// Represents the migration metadata file for a CK model.
/// This file lists all available migrations from previous versions.
/// </summary>
public class CkMigrationMetaDto
{
    /// <summary>
    /// The URI of the schema for CK migration metadata
    /// </summary>
    public const string CkMigrationMetaSchemaUri = "https://schemas.meshmakers.cloud/ck-migration-meta.schema.json";

    /// <summary>
    /// The URI of the schema for the migration meta used for serialization
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = CkMigrationMetaSchemaUri;

    /// <summary>
    /// The CK model ID this migration metadata belongs to (e.g., "System-2.0.0")
    /// </summary>
    public required string CkModelId { get; set; }

    /// <summary>
    /// List of available migrations from previous versions
    /// </summary>
    public List<CkMigrationReferenceDto> Migrations { get; set; } = [];
}

/// <summary>
/// Reference to a migration script within a CK model
/// </summary>
public class CkMigrationReferenceDto
{
    /// <summary>
    /// Source version this migration applies from
    /// </summary>
    public required string FromVersion { get; set; }

    /// <summary>
    /// Target version this migration upgrades to (usually the current CK model version)
    /// </summary>
    public required string ToVersion { get; set; }

    /// <summary>
    /// Path to the migration script file (relative to the migrations folder)
    /// </summary>
    public required string ScriptPath { get; set; }

    /// <summary>
    /// Description of what this migration does
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this migration contains breaking changes that require special handling
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Breaking { get; set; } = false;
}

/// <summary>
/// Represents a CK model migration script that defines how to update runtime entities
/// from one CK model version to another.
/// </summary>
public class CkMigrationScriptDto
{
    /// <summary>
    /// The URI of the schema for CK migration scripts
    /// </summary>
    public const string CkMigrationScriptSchemaUri = "https://schemas.meshmakers.cloud/ck-migration.schema.json";

    /// <summary>
    /// The URI of the schema used for serialization
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = CkMigrationScriptSchemaUri;

    /// <summary>
    /// Source CK model version this migration applies from
    /// </summary>
    public required string SourceVersion { get; set; }

    /// <summary>
    /// Target CK model version this migration updates to
    /// </summary>
    public required string TargetVersion { get; set; }

    /// <summary>
    /// Description of what this migration does
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    /// Conditions that must be met before the migration can run
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMigrationConditionDto>? PreConditions { get; set; }

    /// <summary>
    /// Ordered list of migration steps to execute
    /// </summary>
    public List<CkMigrationStepDto> Steps { get; set; } = [];

    /// <summary>
    /// Validations to run after migration completes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMigrationPostValidationDto>? PostValidations { get; set; }
}

/// <summary>
/// A single step in a CK migration script
/// </summary>
public class CkMigrationStepDto
{
    /// <summary>
    /// Unique identifier for this step within the migration
    /// </summary>
    public required string StepId { get; set; }

    /// <summary>
    /// Human-readable description of what this step does
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    /// The type of action to perform
    /// </summary>
    public required CkMigrationActionType Action { get; set; }

    /// <summary>
    /// Target entities for this step (required for most actions, validated at runtime)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationTargetDto? Target { get; set; }

    /// <summary>
    /// Transform configuration (for Transform action)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationTransformDto? Transform { get; set; }

    /// <summary>
    /// Data to set (for Add/Update actions)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Optional condition for this step to execute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationConditionDto? Condition { get; set; }

    /// <summary>
    /// How to handle conflicts
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationConflictBehavior OnConflict { get; set; } = CkMigrationConflictBehavior.Fail;

    /// <summary>
    /// Whether to continue with subsequent steps if this step fails
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
/// Action types for CK migration steps
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationActionType
{
    /// <summary>
    /// Transform existing entities (change type, rename attributes, etc.)
    /// </summary>
    Transform,

    /// <summary>
    /// Update attributes on existing entities
    /// </summary>
    Update,

    /// <summary>
    /// Delete entities matching the target
    /// </summary>
    Delete,

    /// <summary>
    /// Add new entities
    /// </summary>
    Add
}

/// <summary>
/// Conflict behavior for CK migration steps
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationConflictBehavior
{
    /// <summary>
    /// Fail the step on conflict
    /// </summary>
    Fail,

    /// <summary>
    /// Skip the entity on conflict
    /// </summary>
    Skip,

    /// <summary>
    /// Overwrite with new values
    /// </summary>
    Overwrite
}

/// <summary>
/// Target specification for a CK migration step
/// </summary>
public class CkMigrationTargetDto
{
    /// <summary>
    /// CK type ID to target (e.g., "${System}/Query")
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? CkTypeId { get; set; }

    /// <summary>
    /// Specific runtime entity ID to target
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RtId { get; set; }

    /// <summary>
    /// Well-known name of the entity to target
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RtWellKnownName { get; set; }

    /// <summary>
    /// Filter expression to match entities
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationFilterDto? Filter { get; set; }

    /// <summary>
    /// If false, also include user-created entities (not just blueprint-created)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool BlueprintSourceOnly { get; set; } = false;
}

/// <summary>
/// Filter expression for matching entities
/// </summary>
public class CkMigrationFilterDto
{
    /// <summary>
    /// Attribute name to filter on
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Attribute { get; set; }

    /// <summary>
    /// Comparison operator
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationFilterOperator? Operator { get; set; }

    /// <summary>
    /// Value to compare against
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }

    /// <summary>
    /// AND combination of filters
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMigrationFilterDto>? And { get; set; }

    /// <summary>
    /// OR combination of filters
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMigrationFilterDto>? Or { get; set; }
}

/// <summary>
/// Filter operators for CK migration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationFilterOperator
{
    /// <summary>
    /// Equals
    /// </summary>
    Eq,

    /// <summary>
    /// Not equals
    /// </summary>
    Ne,

    /// <summary>
    /// Attribute exists
    /// </summary>
    Exists,

    /// <summary>
    /// Attribute does not exist
    /// </summary>
    NotExists,

    /// <summary>
    /// Contains substring
    /// </summary>
    Contains,

    /// <summary>
    /// Starts with
    /// </summary>
    StartsWith
}

/// <summary>
/// Transform configuration for CK migration
/// </summary>
public class CkMigrationTransformDto
{
    /// <summary>
    /// Type of transformation
    /// </summary>
    public required CkMigrationTransformType Type { get; set; }

    /// <summary>
    /// Target attribute name (for SetValue, Rename, Delete)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? TargetAttribute { get; set; }

    /// <summary>
    /// Source attribute name (for Rename, Copy)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? SourceAttribute { get; set; }

    /// <summary>
    /// Value to set (for SetValue)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }

    /// <summary>
    /// Value mapping table (for MapValue)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public Dictionary<string, object>? ValueMapping { get; set; }

    /// <summary>
    /// New CK type ID (for ChangeCkType)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? NewCkTypeId { get; set; }
}

/// <summary>
/// Transform types for CK migration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationTransformType
{
    /// <summary>
    /// Change the CK type of entities
    /// </summary>
    ChangeCkType,

    /// <summary>
    /// Set a static value on an attribute
    /// </summary>
    SetValue,

    /// <summary>
    /// Rename an attribute
    /// </summary>
    RenameAttribute,

    /// <summary>
    /// Copy an attribute to a new name
    /// </summary>
    CopyAttribute,

    /// <summary>
    /// Delete an attribute
    /// </summary>
    DeleteAttribute,

    /// <summary>
    /// Map values using a lookup table
    /// </summary>
    MapValue
}

/// <summary>
/// Condition for CK migration execution
/// </summary>
public class CkMigrationConditionDto
{
    /// <summary>
    /// Type of condition
    /// </summary>
    public required CkMigrationConditionType Type { get; set; }

    /// <summary>
    /// Target for entity-based conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationTargetDto? Target { get; set; }

    /// <summary>
    /// CK model ID for version-based conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? CkModelId { get; set; }

    /// <summary>
    /// Version for version-based conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Version { get; set; }

    /// <summary>
    /// Attribute name for attribute conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Attribute { get; set; }

    /// <summary>
    /// Expected value for attribute conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }
}

/// <summary>
/// Condition types for CK migration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationConditionType
{
    /// <summary>
    /// Check if a specific CK model version is installed
    /// </summary>
    CkModelVersionInstalled,

    /// <summary>
    /// Check if entities matching target exist
    /// </summary>
    EntityExists,

    /// <summary>
    /// Check if entities matching target do not exist
    /// </summary>
    EntityNotExists,

    /// <summary>
    /// Check if attribute has expected value
    /// </summary>
    AttributeEquals
}

/// <summary>
/// Post-migration validation
/// </summary>
public class CkMigrationPostValidationDto
{
    /// <summary>
    /// Unique identifier for this validation
    /// </summary>
    public required string ValidationId { get; set; }

    /// <summary>
    /// Description of what is being validated
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    /// Type of validation
    /// </summary>
    public required CkMigrationValidationType Type { get; set; }

    /// <summary>
    /// Target for validation
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationTargetDto? Target { get; set; }

    /// <summary>
    /// Expected count (for EntityCount validation)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int? ExpectedCount { get; set; }

    /// <summary>
    /// Severity if validation fails
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMigrationValidationSeverity Severity { get; set; } = CkMigrationValidationSeverity.Error;
}

/// <summary>
/// Validation types for CK migration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationValidationType
{
    /// <summary>
    /// Validate entity count matches expected
    /// </summary>
    EntityCount,

    /// <summary>
    /// Validate entity exists
    /// </summary>
    EntityExists,

    /// <summary>
    /// Validate no entities of old type remain
    /// </summary>
    NoEntitiesOfType
}

/// <summary>
/// Severity of validation failure
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CkMigrationValidationSeverity
{
    /// <summary>
    /// Validation failure is an error
    /// </summary>
    Error,

    /// <summary>
    /// Validation failure is a warning
    /// </summary>
    Warning
}
