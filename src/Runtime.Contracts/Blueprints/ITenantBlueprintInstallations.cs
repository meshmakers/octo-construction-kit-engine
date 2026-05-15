using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Tracks the set of blueprints that are currently <i>installed</i> on a tenant.
/// This is distinct from <see cref="ITenantBlueprintHistory"/>, which is an
/// append-only audit log of every operation that ever touched the tenant —
/// installations are a live, mutable view of what is in effect right now.
/// </summary>
/// <remarks>
/// A tenant can carry multiple blueprints concurrently (Phase 3 multi-blueprint).
/// One entry per (tenantId, blueprintName) — re-installing or updating a
/// blueprint upserts its single row.
/// </remarks>
public interface ITenantBlueprintInstallations
{
    /// <summary>
    /// Returns every blueprint currently installed on the tenant.
    /// </summary>
    Task<IReadOnlyList<BlueprintInstallation>> GetInstalledAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the installation row for a specific blueprint name on the tenant,
    /// or <c>null</c> when the blueprint is not installed.
    /// </summary>
    Task<BlueprintInstallation?> GetByBlueprintNameAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new installation row or replaces the existing one for the same
    /// blueprint name. Use this after a successful Apply, Update or ReApply.
    /// </summary>
    Task UpsertAsync(
        string tenantId,
        BlueprintInstallation installation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the installation row for a blueprint name on the tenant. Returns
    /// <c>true</c> when a row was removed, <c>false</c> when no such row existed.
    /// </summary>
    Task<bool> RemoveAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One row of <see cref="ITenantBlueprintInstallations"/>. Distinct from
/// <see cref="TenantBlueprintInfo"/> (which models a single Apply event) in
/// that it is a live record — the row mutates as the blueprint is updated.
/// </summary>
public class BlueprintInstallation
{
    /// <summary>
    /// Fully-qualified blueprint id currently installed on the tenant.
    /// </summary>
    public required BlueprintId BlueprintId { get; set; }

    /// <summary>
    /// When the blueprint was first applied to the tenant.
    /// </summary>
    public required DateTime InstalledAt { get; set; }

    /// <summary>
    /// When the installation was last touched (Apply, ReApply or Update).
    /// </summary>
    public required DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// Checksum of the seed data file that was applied, if available. Used by
    /// future tooling to detect drift between the catalog and what is installed.
    /// </summary>
    public string? SeedDataChecksum { get; set; }

    /// <summary>
    /// Blueprint ids that were installed alongside this one as transitive
    /// dependencies. Cleared and re-populated on each Update.
    /// </summary>
    public List<BlueprintId> ResolvedDependencies { get; set; } = [];

    /// <summary>
    /// <c>true</c> when this installation was created as a transitive
    /// dependency of another blueprint (rather than a direct install).
    /// Used by uninstall to decide whether the row can be auto-removed when
    /// the last referrer goes away.
    /// </summary>
    public bool IsDependency { get; set; }
}
