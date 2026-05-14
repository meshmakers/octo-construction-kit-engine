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
    /// Gets update information for a tenant's current blueprint
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Information about available updates, or null if no blueprint is applied</returns>
    Task<BlueprintUpdateInfo?> GetUpdateInfoAsync(
        string tenantId,
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
    /// Restores a tenant from a backup, returning the tenant to the state captured
    /// in the backup. Wraps <see cref="ITenantBackupService.RestoreBackupAsync"/>
    /// with audit-trail notifications.
    /// </summary>
    /// <param name="tenantId">Target tenant identifier</param>
    /// <param name="backupId">Backup identifier to restore from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the rollback</returns>
    Task<BackupRestoreResult> RollbackAsync(
        string tenantId,
        string backupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a blueprint from a tenant. Locked entities owned by the
    /// blueprint are erased; unlocked entities are left as user data
    /// (they may have been adopted by the user after the original apply).
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

    /// <summary>Number of locked entities erased from the tenant.</summary>
    public int EntitiesDeleted { get; set; }

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
