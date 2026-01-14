using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Provides access to runtime repositories for specific tenants.
/// This interface allows services to obtain a repository instance for a given tenant ID.
/// </summary>
public interface IRuntimeRepositoryProvider
{
    /// <summary>
    /// Gets a runtime repository for the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The runtime repository for the tenant, or null if not available</returns>
    Task<IRuntimeRepository?> GetRepositoryAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a repository is available for the specified tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    /// <returns>True if a repository is available for the tenant</returns>
    bool IsRepositoryAvailable(string tenantId);

    /// <summary>
    /// Gets the CK model versions currently installed in the tenant's schema.
    /// This method should be called BEFORE importing new CK models to capture
    /// the current versions for migration purposes.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping model names to their installed version strings</returns>
    Task<IReadOnlyDictionary<string, string>> GetSchemaVersionsAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}
