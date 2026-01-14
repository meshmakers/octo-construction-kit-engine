using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
///     Represents a blueprint migration script that defines how to update from one version to another
/// </summary>
public class BlueprintMigrationDto
{
    /// <summary>
    ///     The URI of the schema for the blueprint migration
    /// </summary>
    public const string BlueprintMigrationSchemaUri = "https://schemas.meshmakers.cloud/blueprint-migration.schema.json";

    /// <summary>
    ///     The URI of the schema for the migration used for serialization
    /// </summary>
    [YamlMember(Alias = "$schema")]
    [JsonPropertyName("$schema")]
    public virtual string SchemaUri { get; } = BlueprintMigrationSchemaUri;

    /// <summary>
    ///     The source blueprint version this migration applies from
    /// </summary>
    public required string SourceVersion { get; set; }

    /// <summary>
    ///     The target blueprint version this migration updates to
    /// </summary>
    public required string TargetVersion { get; set; }

    /// <summary>
    ///     Description of what this migration does
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     Ordered list of migration steps to execute
    /// </summary>
    public List<MigrationStepDto> Steps { get; set; } = [];

    /// <summary>
    ///     Conditions that must be met before the migration can run
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<MigrationConditionDto>? PreConditions { get; set; }

    /// <summary>
    ///     Validations to run after migration completes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<MigrationValidationDto>? PostValidations { get; set; }
}

/// <summary>
///     Represents a single step in a migration script
/// </summary>
public class MigrationStepDto
{
    /// <summary>
    ///     Unique identifier for this step within the migration
    /// </summary>
    public required string StepId { get; set; }

    /// <summary>
    ///     Human-readable description of what this step does
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     The type of action to perform
    /// </summary>
    public required MigrationActionType Action { get; set; }

    /// <summary>
    ///     Target entity or entities for this step
    /// </summary>
    public required EntityTargetDto Target { get; set; }

    /// <summary>
    ///     Data to apply (for add/update actions)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public JsonElement? Data { get; set; }

    /// <summary>
    ///     Transform configuration (for transform actions)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public TransformConfigDto? Transform { get; set; }

    /// <summary>
    ///     Optional condition that must be true for this step to execute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public MigrationConditionDto? Condition { get; set; }

    /// <summary>
    ///     How to handle conflicts if entity already exists
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public MigrationConflictBehavior OnConflict { get; set; } = MigrationConflictBehavior.Fail;

    /// <summary>
    ///     Whether to continue with subsequent steps if this step fails
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
///     The type of action to perform in a migration step
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationActionType
{
    /// <summary>
    ///     Add a new entity
    /// </summary>
    Add,

    /// <summary>
    ///     Update an existing entity
    /// </summary>
    Update,

    /// <summary>
    ///     Delete an entity
    /// </summary>
    Delete,

    /// <summary>
    ///     Rename an entity or attribute
    /// </summary>
    Rename,

    /// <summary>
    ///     Transform entity data using a transformation configuration
    /// </summary>
    Transform
}

/// <summary>
///     How to handle conflicts during migration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationConflictBehavior
{
    /// <summary>
    ///     Skip the operation if conflict detected
    /// </summary>
    Skip,

    /// <summary>
    ///     Overwrite with the new value
    /// </summary>
    Overwrite,

    /// <summary>
    ///     Attempt to merge values
    /// </summary>
    Merge,

    /// <summary>
    ///     Fail the migration on conflict
    /// </summary>
    Fail
}

/// <summary>
///     Target entity or entities for a migration step
/// </summary>
public class EntityTargetDto
{
    /// <summary>
    ///     CK type ID to target
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? CkTypeId { get; set; }

    /// <summary>
    ///     Specific runtime entity ID to target
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RtId { get; set; }

    /// <summary>
    ///     Well-known name of the entity to target
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RtWellKnownName { get; set; }

    /// <summary>
    ///     Filter expression to match multiple entities
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public FilterExpressionDto? Filter { get; set; }

    /// <summary>
    ///     Only target entities created by this blueprint (rtBlueprintSource matches)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool BlueprintSourceOnly { get; set; } = true;
}

/// <summary>
///     Filter expression for matching entities
/// </summary>
public class FilterExpressionDto
{
    /// <summary>
    ///     Attribute name to filter on
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Attribute { get; set; }

    /// <summary>
    ///     Comparison operator
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public FilterOperator? Operator { get; set; }

    /// <summary>
    ///     Value to compare against
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }

    /// <summary>
    ///     AND combination of filters
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<FilterExpressionDto>? And { get; set; }

    /// <summary>
    ///     OR combination of filters
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<FilterExpressionDto>? Or { get; set; }
}

/// <summary>
///     Filter operators for entity matching
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterOperator
{
    /// <summary>
    ///     Equals
    /// </summary>
    Eq,

    /// <summary>
    ///     Not equals
    /// </summary>
    Ne,

    /// <summary>
    ///     Contains substring
    /// </summary>
    Contains,

    /// <summary>
    ///     Starts with
    /// </summary>
    StartsWith,

    /// <summary>
    ///     Ends with
    /// </summary>
    EndsWith,

    /// <summary>
    ///     Attribute exists
    /// </summary>
    Exists,

    /// <summary>
    ///     Attribute does not exist
    /// </summary>
    NotExists
}

/// <summary>
///     Configuration for attribute transformations
/// </summary>
public class TransformConfigDto
{
    /// <summary>
    ///     Type of transformation
    /// </summary>
    public required TransformType Type { get; set; }

    /// <summary>
    ///     Source attribute name
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? SourceAttribute { get; set; }

    /// <summary>
    ///     Target attribute name
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? TargetAttribute { get; set; }

    /// <summary>
    ///     Static value to set
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }

    /// <summary>
    ///     Map of old values to new values
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public Dictionary<string, object>? ValueMapping { get; set; }
}

/// <summary>
///     Types of attribute transformations
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransformType
{
    /// <summary>
    ///     Rename an attribute
    /// </summary>
    Rename,

    /// <summary>
    ///     Copy an attribute to a new name
    /// </summary>
    Copy,

    /// <summary>
    ///     Delete an attribute
    /// </summary>
    Delete,

    /// <summary>
    ///     Set a static value
    /// </summary>
    SetValue,

    /// <summary>
    ///     Map values using a lookup table
    /// </summary>
    MapValue
}

/// <summary>
///     Condition for migration execution
/// </summary>
public class MigrationConditionDto
{
    /// <summary>
    ///     Type of condition
    /// </summary>
    public required MigrationConditionType Type { get; set; }

    /// <summary>
    ///     Entity target for the condition
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public EntityTargetDto? Target { get; set; }

    /// <summary>
    ///     Attribute name for attribute conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Attribute { get; set; }

    /// <summary>
    ///     Expected value for the condition
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? Value { get; set; }

    /// <summary>
    ///     Custom expression for complex conditions
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Expression { get; set; }
}

/// <summary>
///     Types of migration conditions
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationConditionType
{
    /// <summary>
    ///     Entity must exist
    /// </summary>
    EntityExists,

    /// <summary>
    ///     Entity must not exist
    /// </summary>
    EntityNotExists,

    /// <summary>
    ///     Attribute must equal a specific value
    /// </summary>
    AttributeEquals,

    /// <summary>
    ///     Custom expression evaluation
    /// </summary>
    Custom
}

/// <summary>
///     Validation to run after migration
/// </summary>
public class MigrationValidationDto
{
    /// <summary>
    ///     Unique identifier for this validation
    /// </summary>
    public required string ValidationId { get; set; }

    /// <summary>
    ///     Description of what is being validated
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     Type of validation
    /// </summary>
    public required MigrationValidationType Type { get; set; }

    /// <summary>
    ///     Target for validation
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public EntityTargetDto? Target { get; set; }

    /// <summary>
    ///     Expected count (for entityCount validation)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int? ExpectedCount { get; set; }

    /// <summary>
    ///     Expected value (for attributeValue validation)
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public object? ExpectedValue { get; set; }

    /// <summary>
    ///     Severity level if validation fails
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public MigrationValidationSeverity Severity { get; set; } = MigrationValidationSeverity.Error;
}

/// <summary>
///     Types of migration validations
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationValidationType
{
    /// <summary>
    ///     Validate entity count matches expected
    /// </summary>
    EntityCount,

    /// <summary>
    ///     Validate entity exists
    /// </summary>
    EntityExists,

    /// <summary>
    ///     Validate attribute has expected value
    /// </summary>
    AttributeValue,

    /// <summary>
    ///     Validate referential integrity
    /// </summary>
    ReferenceIntegrity
}

/// <summary>
///     Severity of validation failure
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MigrationValidationSeverity
{
    /// <summary>
    ///     Validation failure is an error
    /// </summary>
    Error,

    /// <summary>
    ///     Validation failure is a warning
    /// </summary>
    Warning
}
