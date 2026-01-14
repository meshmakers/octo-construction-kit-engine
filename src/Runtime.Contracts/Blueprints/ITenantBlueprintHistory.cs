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
    /// Gets the currently active blueprint for a tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current blueprint info, or null if no blueprint was applied</returns>
    Task<TenantBlueprintInfo?> GetCurrentAsync(
        string tenantId,
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
