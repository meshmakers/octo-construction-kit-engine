using System;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IArchiveLifecycleService"/> implementation. Pure orchestration of the
/// CkArchive state machine on top of <see cref="ICkArchiveRuntimeStore"/>,
/// <see cref="IStreamDataRepository"/>, and <see cref="IArchiveAuditTrail"/>; no DB-specific code
/// lives here.
/// </summary>
/// <remarks>
/// Operation ordering follows concept §11: when both the data store (CrateDB) and the entity store
/// (MongoDB) are touched, Crate is updated first and the entity store last. Because the Crate
/// operations are idempotent (<c>EnsureArchiveCreatedAsync</c> uses <c>CREATE TABLE IF NOT EXISTS</c>
/// and <c>DeleteArchiveAsync</c> uses <c>DROP TABLE IF EXISTS</c>) a retry after a transient
/// failure on the entity-store update converges cleanly without leaving partial state visible to
/// callers (the status-first check keeps inserts and queries gated regardless).
/// </remarks>
/// <summary>
/// Default implementation of <see cref="IArchiveLifecycleService"/>. Public so per-tenant hosts
/// (e.g. the MongoDB tenant context) can construct it directly with tenant-scoped dependencies
/// without going through DI registration.
/// </summary>
public sealed class ArchiveLifecycleService : IArchiveLifecycleService
{
    private readonly string _tenantId;
    private readonly ICkArchiveRuntimeStore _store;
    private readonly IStreamDataRepository _repository;
    private readonly IArchiveAuditTrail _audit;
    private readonly ILogger<ArchiveLifecycleService> _logger;

    /// <summary>
    /// Constructs the lifecycle service. The store and stream-data repository must be
    /// tenant-scoped; the audit trail can be either tenant-scoped or shared (the tenant id is
    /// passed explicitly into every audit call).
    /// </summary>
    public ArchiveLifecycleService(
        string tenantId,
        ICkArchiveRuntimeStore store,
        IStreamDataRepository repository,
        IArchiveAuditTrail audit,
        ILogger<ArchiveLifecycleService> logger)
    {
        _tenantId = tenantId;
        _store = store;
        _repository = repository;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ActivateAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await LoadAsync(archiveRtId);

        switch (snapshot.Status)
        {
            case CkArchiveStatus.Activated:
                return; // idempotent
            case CkArchiveStatus.Created:
            case CkArchiveStatus.Disabled:
            case CkArchiveStatus.Failed:
                break;
            default:
                throw new InvalidArchiveStateTransitionException(archiveRtId, snapshot.Status, "activate");
        }

        await EnsureCrateProvisionedAsync(snapshot);
        await TransitionAsync(snapshot, CkArchiveStatus.Activated);
    }

    /// <inheritdoc />
    public async Task DisableAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await LoadAsync(archiveRtId);

        if (snapshot.Status == CkArchiveStatus.Disabled) return; // idempotent
        if (snapshot.Status != CkArchiveStatus.Activated)
        {
            throw new InvalidArchiveStateTransitionException(archiveRtId, snapshot.Status, "disable");
        }

        await TransitionAsync(snapshot, CkArchiveStatus.Disabled);
    }

    /// <inheritdoc />
    public Task EnableAsync(OctoObjectId archiveRtId) => ActivateAsync(archiveRtId);

    /// <inheritdoc />
    public async Task RetryActivationAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await LoadAsync(archiveRtId);

        if (snapshot.Status != CkArchiveStatus.Failed)
        {
            throw new InvalidArchiveStateTransitionException(archiveRtId, snapshot.Status, "retry activation of");
        }

        await EnsureCrateProvisionedAsync(snapshot);
        await TransitionAsync(snapshot, CkArchiveStatus.Activated);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await LoadAsync(archiveRtId);

        // Crate first (idempotent), entity store last; matches §11.
        await _repository.DeleteArchiveAsync(archiveRtId);
        await _store.ArchiveEntityAsync(archiveRtId);
        await _audit.RecordDeletionAsync(_tenantId, archiveRtId, snapshot.Status);
    }

    private async Task<CkArchiveSnapshot> LoadAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await _store.GetAsync(archiveRtId);
        if (snapshot is null)
        {
            throw new ArchiveNotFoundException(archiveRtId);
        }
        return snapshot;
    }

    private async Task EnsureCrateProvisionedAsync(CkArchiveSnapshot snapshot)
    {
        try
        {
            await _repository.EnsureArchiveCreatedAsync(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to provision Crate table for archive {ArchiveRtId} (was {FromStatus})",
                snapshot.RtId, snapshot.Status);

            // Best-effort flip to Failed so the studio reflects reality. If this also fails the
            // outer exception still surfaces; the next reconciliation pass (T23) closes the loop.
            try
            {
                await _store.SetStatusAsync(snapshot.RtId, CkArchiveStatus.Failed);
                await _audit.RecordTransitionAsync(_tenantId, snapshot.RtId, snapshot.Status, CkArchiveStatus.Failed, ex.Message);
            }
            catch (Exception bookkeepingEx)
            {
                _logger.LogError(bookkeepingEx,
                    "Failed to record Failed status for archive {ArchiveRtId} after activation error",
                    snapshot.RtId);
            }

            throw new ArchiveActivationFailedException(snapshot.RtId, ex);
        }
    }

    private async Task TransitionAsync(CkArchiveSnapshot from, CkArchiveStatus toStatus)
    {
        await _store.SetStatusAsync(from.RtId, toStatus);
        await _audit.RecordTransitionAsync(_tenantId, from.RtId, from.Status, toStatus, reason: null);
    }
}
