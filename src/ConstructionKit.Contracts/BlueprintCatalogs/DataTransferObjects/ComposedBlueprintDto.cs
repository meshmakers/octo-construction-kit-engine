namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
/// Represents the result of composing multiple blueprints
/// </summary>
public class ComposedBlueprintDto
{
    /// <summary>
    /// The root blueprint that was composed
    /// </summary>
    public required BlueprintId RootBlueprintId { get; init; }

    /// <summary>
    /// All CK model dependencies merged from the blueprint hierarchy
    /// </summary>
    public required List<CkModelIdVersionRange> CkModelDependencies { get; init; }

    /// <summary>
    /// References to seed data files in order of application (base blueprints first)
    /// </summary>
    public required List<SeedDataReferenceDto> SeedDataReferences { get; init; }

    /// <summary>
    /// All resolved blueprints in the composition (base blueprints first)
    /// </summary>
    public required List<BlueprintMetaRootDto> ResolvedBlueprints { get; init; }

    /// <summary>
    /// Available migration scripts from the root blueprint
    /// </summary>
    public List<MigrationReferenceDto> AvailableMigrations { get; init; } = [];
}

/// <summary>
/// Reference to a migration script from a specific blueprint
/// </summary>
public class MigrationReferenceDto
{
    /// <summary>
    /// The blueprint that contains this migration
    /// </summary>
    public required BlueprintId BlueprintId { get; init; }

    /// <summary>
    /// The source version this migration applies from
    /// </summary>
    public required string FromVersion { get; init; }

    /// <summary>
    /// The path to the migration script file
    /// </summary>
    public required string ScriptPath { get; init; }

    /// <summary>
    /// The absolute path to the migration script file (resolved during composition)
    /// </summary>
    public string? ResolvedPath { get; set; }

    /// <summary>
    /// Optional description of the migration
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Reference to a seed data file from a specific blueprint
/// </summary>
public class SeedDataReferenceDto
{
    /// <summary>
    /// The blueprint that contains this seed data
    /// </summary>
    public required BlueprintId BlueprintId { get; init; }

    /// <summary>
    /// The path to the seed data file
    /// </summary>
    public required string SeedDataPath { get; init; }

    /// <summary>
    /// The absolute path to the seed data file (resolved during composition)
    /// </summary>
    public string? ResolvedPath { get; set; }
}
