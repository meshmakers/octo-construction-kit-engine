using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Service for applying blueprints to tenants
/// </summary>
public interface IBlueprintService
{
    /// <summary>
    /// Applies a blueprint to initialize a tenant with CK models and seed data.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="blueprintId">Blueprint to apply</param>
    /// <param name="force">
    /// If <c>true</c>, re-apply seed data via upsert even if the same version is
    /// already recorded for the tenant (recovery after storage corruption or
    /// manual cleanup). The recorded application mode is then
    /// <see cref="BlueprintApplicationMode.ReApply"/> instead of
    /// <see cref="BlueprintApplicationMode.Initial"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the blueprint application</returns>
    Task<BlueprintApplicationResult> ApplyBlueprintAsync(
        string tenantId,
        BlueprintId blueprintId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a blueprint like <see cref="ApplyBlueprintAsync(string, BlueprintId, bool, CancellationToken)"/>, with
    /// the tenant upgrade guard of the CK models the blueprint installs or raises (CK v2 F2.5, AB#6871).
    /// </summary>
    /// <remarks>
    /// With <see cref="BlueprintApplyOptions.CkUpgradeGuard"/> = <see cref="BlueprintCkUpgradeGuardMode.Enforce"/> the
    /// apply stops before the first change when the CK update set would break an installed model: the result is not
    /// successful, <see cref="BlueprintApplicationResult.IsCkUpgradeBlocked"/> is <c>true</c> and
    /// <see cref="BlueprintApplicationResult.CkUpgradeBlockers"/> names what would break. The tenant and the
    /// installation records are unchanged. A host without an <see cref="IBlueprintCkUpgradeGuard"/> applies unguarded.
    /// </remarks>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="blueprintId">Blueprint to apply</param>
    /// <param name="options">Re-apply and guard options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the blueprint application</returns>
    Task<BlueprintApplicationResult> ApplyBlueprintAsync(
        string tenantId,
        BlueprintId blueprintId,
        BlueprintApplyOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a blueprint can be applied (checks CK dependencies exist, seed data is valid, etc.)
    /// </summary>
    /// <param name="blueprintId">Blueprint to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the validation</returns>
    Task<BlueprintValidationResult> ValidateBlueprintAsync(
        BlueprintId blueprintId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available blueprints
    /// </summary>
    /// <param name="skip">Number of blueprints to skip</param>
    /// <param name="take">Number of blueprints to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available blueprints</returns>
    Task<BlueprintListResult> ListBlueprintsAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for blueprints by name or description
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <param name="skip">Number of blueprints to skip</param>
    /// <param name="take">Number of blueprints to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results</returns>
    Task<BlueprintSearchResult> SearchBlueprintsAsync(
        string searchTerm,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets update information for the blueprint that was applied to the tenant last.
    /// </summary>
    /// <remarks>
    /// On a multi-blueprint tenant "the blueprint applied last" is arbitrary from the
    /// caller's point of view - prefer the overload taking a blueprint name.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Information about available updates, or null if no blueprint is applied</returns>
    Task<BlueprintUpdateInfo?> GetUpdateInfoAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets update information for one specific blueprint on the tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="blueprintName">
    /// Blueprint name to describe, without the version suffix. When null, empty or
    /// whitespace the blueprint that was applied to the tenant last is used (legacy
    /// behaviour).
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Information about available updates, or null when that blueprint is not installed
    /// on the tenant
    /// </returns>
    Task<BlueprintUpdateInfo?> GetUpdateInfoAsync(
        string tenantId,
        string? blueprintName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews the changes that would be made by updating to a specific blueprint version
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="targetVersion">Target blueprint version</param>
    /// <param name="updateMode">How to apply the update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preview of changes including conflicts</returns>
    Task<BlueprintUpdatePreview> PreviewUpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode updateMode = BlueprintUpdateMode.Merge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an update to a tenant's blueprint
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="targetVersion">Target blueprint version</param>
    /// <param name="updateMode">How to apply the update</param>
    /// <param name="options">Update options including conflict resolutions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the update operation</returns>
    Task<BlueprintUpdateResult> ApplyUpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode updateMode = BlueprintUpdateMode.Merge,
        BlueprintUpdateOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the blueprint application history for a tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of blueprint applications in chronological order</returns>
    Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a blueprint from a tenant. Locked entities owned by the
    /// blueprint are erased. Tenant-owned seed entities (the seed sets
    /// <c>rtBlueprintLocked: false</c>, AB#8966) are erased when the tenant
    /// never edited them (their content still equals the seed of the
    /// installed version) and nothing outside the uninstall references them;
    /// the others are kept and listed in
    /// <see cref="BlueprintUninstallResult.TenantOwnedKept"/> with the reason.
    /// Other unlocked entities are left as user data.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier.</param>
    /// <param name="blueprintName">Name of the blueprint to uninstall.</param>
    /// <param name="cascade">
    /// When <c>true</c>, transitive dependencies of the uninstalled blueprint
    /// that were originally installed as dependencies (IsDependency = true)
    /// and that no other installed blueprint still references are uninstalled
    /// in the same pass. When <c>false</c>, dependencies are left in place.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="BlueprintUninstallResult"/> describing how many entities were
    /// removed, which dependencies were cascaded, and any conflicts that
    /// prevented the uninstall (e.g. other installed blueprints still
    /// depend on the requested target).
    /// </returns>
    Task<BlueprintUninstallResult> UninstallAsync(
        string tenantId,
        string blueprintName,
        bool cascade = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AB#8966: the dry run of <see cref="UninstallAsync"/>. Runs the same checks and the same
    /// classification but changes nothing: no entity is erased, no installation row removed and no
    /// notification sent. <see cref="BlueprintUninstallResult.DryRun"/> is <c>true</c>,
    /// <see cref="BlueprintUninstallResult.EntitiesDeleted"/> and
    /// <see cref="BlueprintUninstallResult.DeletedEntities"/> name what the uninstall would erase,
    /// <see cref="BlueprintUninstallResult.TenantOwnedKept"/> what it would keep.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier.</param>
    /// <param name="blueprintName">Name of the blueprint to uninstall.</param>
    /// <param name="cascade">As for <see cref="UninstallAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<BlueprintUninstallResult> PreviewUninstallAsync(
        string tenantId,
        string blueprintName,
        bool cascade = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of <see cref="IBlueprintService.UninstallAsync"/>.
/// </summary>
public class BlueprintUninstallResult
{
    /// <summary>Whether the uninstall completed without errors.</summary>
    public bool Success { get; set; }

    /// <summary>
    /// Blueprint that was uninstalled (the explicit target, not the cascaded
    /// dependencies). Null when the operation was rejected before any state
    /// changed.
    /// </summary>
    public BlueprintId? UninstalledBlueprintId { get; set; }

    /// <summary>
    /// <c>true</c> for the result of <see cref="IBlueprintService.PreviewUninstallAsync"/>: nothing was changed, the
    /// counts and lists say what the uninstall would do.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Number of entities erased from the tenant: the locked entities of the blueprint and its unedited,
    /// unreferenced tenant-owned seed entities (AB#8966). In a dry run: the number that would be erased.
    /// </summary>
    public int EntitiesDeleted { get; set; }

    /// <summary>
    /// AB#8966: the entities counted in <see cref="EntitiesDeleted"/>, cascaded blueprints included.
    /// </summary>
    public List<BlueprintUninstallDeletedEntity> DeletedEntities { get; set; } = [];

    /// <summary>
    /// AB#8966: tenant-owned seed entities of the blueprint that stay in the tenant, each with the reason
    /// (edited by the tenant, referenced by an entity that stays, not comparable with the seed, or its erase failed).
    /// They are re-adopted when the blueprint is installed again.
    /// </summary>
    public List<BlueprintTenantOwnedKeptEntity> TenantOwnedKept { get; set; } = [];

    /// <summary>
    /// Blueprint ids that were cascade-uninstalled because they were marked
    /// IsDependency = true and no longer referenced. Empty when the caller
    /// did not request cascade or no orphan deps were found.
    /// </summary>
    public List<BlueprintId> CascadedDependencies { get; set; } = [];

    /// <summary>
    /// Other installed blueprints that still depend on the target. Populated
    /// when uninstall is blocked because <see cref="Success"/> is false and
    /// the caller did not pass <c>cascade=true</c>.
    /// </summary>
    public List<BlueprintId> BlockingDependents { get; set; } = [];

    /// <summary>Errors emitted during the operation.</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>Warnings emitted during the operation.</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// AB#8966: an entity an uninstall erased (or, in a dry run, would erase).
/// </summary>
public class BlueprintUninstallDeletedEntity : BlueprintTenantOwnedEntity
{
    /// <summary>
    /// <c>true</c> for a tenant-owned seed entity erased because the tenant never edited it; <c>false</c> for a
    /// locked entity of the blueprint.
    /// </summary>
    public bool TenantOwned { get; set; }
}

/// <summary>
/// AB#8966: a tenant-owned seed entity an uninstall keeps, with the reason.
/// </summary>
public class BlueprintTenantOwnedKeptEntity : BlueprintTenantOwnedEntity
{
    /// <summary>Why the entity is kept.</summary>
    public BlueprintTenantOwnedKeptReason Reason { get; set; }

    /// <summary>
    /// For <see cref="BlueprintTenantOwnedKeptReason.Referenced"/>: the runtime ids of the entities outside the
    /// uninstall that hold an association to it. Empty otherwise.
    /// </summary>
    public List<string> ReferencedBy { get; set; } = [];
}

/// <summary>
/// AB#8966: why an uninstall keeps a tenant-owned seed entity.
/// </summary>
public enum BlueprintTenantOwnedKeptReason
{
    /// <summary>
    /// The tenant edited it: its attributes or its well-known name differ from the seed of the installed version (a name
    /// added to an entity the seed leaves unnamed counts), or it is held under another runtime id than the seed gives it.
    /// </summary>
    Edited,

    /// <summary>
    /// Unedited, but it shares an association with an entity that stays in the tenant: any association with an entity of
    /// the blueprint's seed that stays, or one with an entity outside that seed (created by the tenant, owned by another
    /// blueprint) that is not the entity's own seed-declared link. Erasing it would remove that association.
    /// </summary>
    Referenced,

    /// <summary>
    /// Whether the tenant edited it cannot be decided (the CK type is not in the cache, or the comparison failed), so
    /// the tenant content is kept.
    /// </summary>
    NotComparable,

    /// <summary>
    /// Unedited and meant to be erased, but the erase failed (the reason is in the warnings); it stays in the tenant.
    /// </summary>
    EraseFailed
}
