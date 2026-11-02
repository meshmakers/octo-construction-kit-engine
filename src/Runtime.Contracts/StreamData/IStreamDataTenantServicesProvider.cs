using System.Threading;
using System.Threading.Tasks;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolves the tenant-scoped stream-data services for code that runs outside a tenant context, such as the blueprint
/// engine (AB#7991): after a blueprint seed created new archives, the engine activates them and queues the backfill of
/// new rollups through these services. The storage backend registers the implementation; a host without one installs
/// blueprints as before and leaves new archives <see cref="CkArchiveStatus.Created" />.
/// </summary>
public interface IStreamDataTenantServicesProvider
{
    /// <summary>
    /// Returns the stream-data services of <paramref name="tenantId" />, or <c>null</c> when the tenant is unknown or no
    /// stream-data backend is registered.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<StreamDataTenantServices?> GetAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The stream-data services of one tenant (AB#7991).
/// </summary>
/// <param name="IsStreamDataEnabled">
/// <c>true</c> when stream data is switched on for the tenant. When <c>false</c> no archive is activated.
/// </param>
/// <param name="LifecycleService">Activates archives; <c>null</c> when the backend cannot provide one.</param>
/// <param name="ArchiveStore">Reads archive snapshots (status, storage shape).</param>
/// <param name="RollupStore">Reads rollup snapshots (sources, aggregations); <c>null</c> without rollup support.</param>
/// <param name="RecomputeOrchestrator">Queues rollup backfills; <c>null</c> without recompute support.</param>
public sealed record StreamDataTenantServices(
    bool IsStreamDataEnabled,
    IArchiveLifecycleService? LifecycleService,
    IArchiveRuntimeStore? ArchiveStore,
    IRollupArchiveRuntimeStore? RollupStore,
    IRecomputeOrchestrator? RecomputeOrchestrator);
