using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Information about a blueprint application to a tenant
/// </summary>
public class TenantBlueprintInfo
{
    /// <summary>
    /// The blueprint that was applied
    /// </summary>
    public required BlueprintId BlueprintId { get; set; }

    /// <summary>
    /// When the blueprint was applied
    /// </summary>
    public required DateTime AppliedAt { get; set; }

    /// <summary>
    /// How the blueprint was applied
    /// </summary>
    public BlueprintApplicationMode ApplicationMode { get; set; }

    /// <summary>
    /// Checksum of the seed data that was applied (for change detection)
    /// </summary>
    public string? SeedDataChecksum { get; set; }

    /// <summary>
    /// Previous blueprint version (for updates/migrations)
    /// </summary>
    public BlueprintId? PreviousVersion { get; set; }

    /// <summary>
    /// Number of entities created during this application
    /// </summary>
    public int EntitiesCreated { get; set; }

    /// <summary>
    /// Number of entities updated during this application
    /// </summary>
    public int EntitiesUpdated { get; set; }

    /// <summary>
    /// Number of entities deleted during this application
    /// </summary>
    public int EntitiesDeleted { get; set; }

    /// <summary>
    /// User or system that triggered the application
    /// </summary>
    public string? AppliedBy { get; set; }

    /// <summary>
    /// Optional notes about the application
    /// </summary>
    public string? Notes { get; set; }
}
