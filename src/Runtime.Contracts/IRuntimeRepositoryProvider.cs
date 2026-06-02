using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Contracts;

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

    /// <summary>
    /// Ensures the specified CK model is installed in the tenant's schema. Idempotent:
    /// if the exact model id is already present, this is a no-op apart from running any
    /// pending migrations. Otherwise the compiled model is fetched from a catalog and
    /// written into the tenant database, and the in-memory CK cache is invalidated so the
    /// next access reloads the model graph.
    /// </summary>
    /// <param name="tenantId">The tenant identifier</param>
    /// <param name="modelId">The concrete CK model id (name + exact version) to install</param>
    /// <param name="operationResult">Collects validation messages from catalog lookup and install</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task EnsureCkModelInstalledAsync(
        string tenantId,
        CkModelId modelId,
        OperationResult operationResult,
        CancellationToken cancellationToken = default);
}
