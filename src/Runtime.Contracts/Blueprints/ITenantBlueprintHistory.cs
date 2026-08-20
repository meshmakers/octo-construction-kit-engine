namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Service for tracking blueprint application history per tenant
/// </summary>
public interface ITenantBlueprintHistory
{
    /// <summary>
    /// Gets the complete blueprint application history for a tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of blueprint applications in chronological order</returns>
    Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent history entry of the tenant, across <i>all</i> blueprints.
    /// </summary>
    /// <remarks>
    /// A tenant can host any number of blueprints concurrently, so "the last entry of the
    /// tenant" says nothing about a specific blueprint - on a multi-blueprint tenant this
    /// returns whichever blueprint happened to be applied last. Any caller that means
    /// "the version of blueprint X currently in effect" has to use
    /// <see cref="GetCurrentByBlueprintNameAsync" /> instead.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Most recent blueprint info, or null if no blueprint was applied</returns>
    Task<TenantBlueprintInfo?> GetCurrentAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent history entry of one specific blueprint on the tenant.
    /// </summary>
    /// <remarks>
    /// This is the lookup the update path needs: it answers "which version of this
    /// blueprint is in effect", independently of what else was applied to the tenant
    /// afterwards. Counterpart of
    /// <see cref="ITenantBlueprintInstallations.GetByBlueprintNameAsync" /> on the
    /// live-state side.
    /// </remarks>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="blueprintName">Blueprint name, without the version suffix</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>
    /// Most recent blueprint info of that blueprint, or null when the blueprint was never
    /// applied to the tenant
    /// </returns>
    Task<TenantBlueprintInfo?> GetCurrentByBlueprintNameAsync(
        string tenantId,
        string blueprintName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new blueprint application
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="info">Blueprint application info</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddEntryAsync(
        string tenantId,
        TenantBlueprintInfo info,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tenant has any blueprint applied
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a blueprint was applied to this tenant</returns>
    Task<bool> HasBlueprintAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
