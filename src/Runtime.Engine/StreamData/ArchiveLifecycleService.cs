using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IArchiveLifecycleService"/> implementation. Pure orchestration of the
/// CkArchive state machine on top of <see cref="IArchiveRuntimeStore"/>,
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
    private readonly IArchiveRuntimeStore _store;
    private readonly IStreamDataRepository _repository;
    private readonly IArchiveAuditTrail _audit;
    private readonly IRollupArchiveRuntimeStore? _rollupStore;
    private readonly ILogger<ArchiveLifecycleService> _logger;
    private readonly Func<DateTime> _clock;

    /// <summary>
    /// Constructs the lifecycle service. The store and stream-data repository must be
    /// tenant-scoped; the audit trail can be either tenant-scoped or shared (the tenant id is
    /// passed explicitly into every audit call). <paramref name="rollupStore"/> is optional;
    /// when supplied, <see cref="DeleteAsync"/> rejects deletion of source archives that still
    /// have active rollups attached (rollup-archives concept §6, §10) and rollup archives get
    /// their <see cref="RollupArchiveSnapshot.LastAggregatedBucketEnd"/> initialised on the
    /// first activation (rollup-archives concept §4).
    /// </summary>
    public ArchiveLifecycleService(
        string tenantId,
        IArchiveRuntimeStore store,
        IStreamDataRepository repository,
        IArchiveAuditTrail audit,
        ILogger<ArchiveLifecycleService> logger,
        IRollupArchiveRuntimeStore? rollupStore = null,
        Func<DateTime>? clock = null)
    {
        _tenantId = tenantId;
        _store = store;
        _repository = repository;
        _audit = audit;
        _rollupStore = rollupStore;
        _logger = logger;
        _clock = clock ?? (() => DateTime.UtcNow);
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

        await ValidateRollupForActivationAsync(archiveRtId);
        await EnsureCrateProvisionedAsync(snapshot);
        await EnsureRollupWatermarkInitialisedAsync(archiveRtId);
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

        await ValidateRollupForActivationAsync(archiveRtId);
        await EnsureCrateProvisionedAsync(snapshot);
        await EnsureRollupWatermarkInitialisedAsync(archiveRtId);
        await TransitionAsync(snapshot, CkArchiveStatus.Activated);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await LoadAsync(archiveRtId);

        // Rollup-archives concept §6 / §10: reject source-archive delete while non-soft-deleted
        // rollups still reference it. The guard is skipped silently when no rollup store is
        // registered (deployments without rollups configured), so it costs nothing for the
        // rollup-free path.
        if (_rollupStore is not null)
        {
            var dependentRollups = await _rollupStore.CountActiveRollupsForSourceAsync(archiveRtId);
            if (dependentRollups > 0)
            {
                throw new RollupSourceInUseException(archiveRtId, dependentRollups);
            }
        }

        using var activity = StreamDataDiagnostics.ActivitySource.StartActivity("archive.delete");
        activity?.SetTag("streamdata.archive.rtid", archiveRtId.ToString());
        activity?.SetTag("streamdata.archive.from_status", snapshot.Status.ToString());

        // Crate first (idempotent), entity store last; matches §11.
        await _repository.DeleteArchiveAsync(archiveRtId);
        await _store.ArchiveEntityAsync(archiveRtId);
        await _audit.RecordDeletionAsync(_tenantId, archiveRtId, snapshot.Status);

        StreamDataDiagnostics.Deletions.Add(1,
            new("archive", archiveRtId.ToString()),
            new("from_status", snapshot.Status.ToString()));
    }

    /// <inheritdoc />
    public async Task AddComputedColumnAsync(
        OctoObjectId archiveRtId, string name, string formula, FormulaResultType resultType, bool indexed)
    {
        var snapshot = await LoadAsync(archiveRtId);
        if (snapshot.Status != CkArchiveStatus.Activated)
        {
            throw new InvalidArchiveStateTransitionException(archiveRtId, snapshot.Status, "add a computed column to");
        }

        var newColumn = new CkArchiveColumnSpec(string.Empty, indexed, Required: false)
        {
            Name = name,
            Formula = formula,
            ResultType = resultType,
            ComputedState = ComputedColumnState.Pending,
            ComputedVersion = 0,
        };

        // Validate the prospective full set (existing + new) before any persistence or DDL.
        var prospective = snapshot.Columns.Append(newColumn).ToList();
        await _repository.ValidateComputedColumnsAsync(archiveRtId, prospective);

        using var activity = StreamDataDiagnostics.ActivitySource.StartActivity("archive.addComputedColumn");
        activity?.SetTag("streamdata.archive.rtid", archiveRtId.ToString());
        activity?.SetTag("streamdata.computedcolumn.name", name);

        // Persist Pending, add the physical column, backfill while the column stays hidden, then flip
        // to Active — the single SetComputedColumnStateAsync(Active) is the atomic switch (§8).
        await _store.AddComputedColumnAsync(archiveRtId, newColumn);
        var withColumn = await LoadAsync(archiveRtId);

        try
        {
            await _repository.AddComputedColumnStorageAsync(withColumn, name);
            await _store.SetComputedColumnStateAsync(archiveRtId, name, ComputedColumnState.Backfilling);
            await _repository.BackfillComputedColumnAsync(withColumn, name);
            await _store.SetComputedColumnStateAsync(archiveRtId, name, ComputedColumnState.Active);
            _logger.LogInformation(
                "Computed column '{Column}' added to archive {Archive} and backfilled (now Active).",
                name, archiveRtId);
        }
        catch (Exception ex)
        {
            await _store.SetComputedColumnStateAsync(archiveRtId, name, ComputedColumnState.Failed);
            _logger.LogError(ex,
                "Backfill of computed column '{Column}' on archive {Archive} failed; column marked Failed.",
                name, archiveRtId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task RemoveComputedColumnAsync(OctoObjectId archiveRtId, string name)
    {
        var snapshot = await LoadAsync(archiveRtId);

        var target = snapshot.Columns.FirstOrDefault(
            c => c.IsComputed && string.Equals(c.Name, name, StringComparison.Ordinal));
        if (target is null)
        {
            return; // idempotent: already gone (or never a computed column)
        }

        // Validate the post-removal set so a now-dangling reference from another computed column is
        // rejected before we mutate anything (concept §9 / §14).
        var remaining = snapshot.Columns
            .Where(c => !(c.IsComputed && string.Equals(c.Name, name, StringComparison.Ordinal)))
            .ToList();
        await _repository.ValidateComputedColumnsAsync(archiveRtId, remaining);

        await _store.RemoveComputedColumnAsync(archiveRtId, name);
        _logger.LogInformation("Computed column '{Column}' removed from archive {Archive}.", name, archiveRtId);
    }

    private async Task<ArchiveSnapshot> LoadAsync(OctoObjectId archiveRtId)
    {
        var snapshot = await _store.GetAsync(archiveRtId);
        if (snapshot is null)
        {
            throw new ArchiveNotFoundException(archiveRtId);
        }
        return snapshot;
    }

    /// <summary>
    /// Runs the rollup-archives concept §10 activation-time validation when the archive is a
    /// rollup. No-op when no rollup store is wired or when the archive is not a rollup. Throws
    /// the matching <see cref="StreamDataException"/> subclass on the first violation; the
    /// caller surfaces the exception via the GraphQL error mapper.
    /// </summary>
    private async Task ValidateRollupForActivationAsync(OctoObjectId archiveRtId)
    {
        if (_rollupStore is null) return;

        var rollup = await _rollupStore.GetAsync(archiveRtId);
        if (rollup is null) return; // not a rollup, or soft-deleted

        var source = await _store.GetAsync(rollup.SourceArchiveRtId);
        RollupValidator.ValidateForActivation(rollup, source);
    }

    /// <summary>
    /// Seeds <see cref="RollupArchiveSnapshot.LastAggregatedBucketEnd"/> when a rollup archive
    /// transitions to Activated for the first time. No-op when the archive is not a rollup, when
    /// no rollup store is wired, or when the watermark has already been set (re-activation after
    /// Disabled/Failed must preserve progress). Concept §4.
    /// </summary>
    /// <remarks>
    /// MVP policy: initial watermark = <c>truncate(now - BucketSize)</c> down to the bucket
    /// boundary. Historical source rows that predate activation are deliberately not back-filled
    /// — call <c>rewindRollupWatermark</c> to opt in. The "scan the source for the smallest
    /// timestamp" variant from the concept doc waits on a dedicated repository method.
    /// </remarks>
    private async Task EnsureRollupWatermarkInitialisedAsync(OctoObjectId archiveRtId)
    {
        if (_rollupStore is null) return;

        var rollup = await _rollupStore.GetAsync(archiveRtId);
        if (rollup is null) return; // not a rollup; or soft-deleted
        if (rollup.LastAggregatedBucketEnd is not null) return; // re-activate after disable/failed

        if (rollup.BucketSize <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Rollup {RollupRtId}: BucketSize is non-positive ({BucketSize}); skipping initial watermark seed.",
                archiveRtId, rollup.BucketSize);
            return;
        }

        var now = _clock();
        var initialBucketEnd = BucketBoundary.InitialWatermark(now, rollup.BucketAlignment, rollup.BucketSize);

        await _rollupStore.AdvanceWatermarkAsync(archiveRtId, initialBucketEnd);

        _logger.LogInformation(
            "Rollup {RollupRtId}: initial watermark seeded to {Watermark:O} (bucketSize={BucketSize}, alignment={Alignment})",
            archiveRtId, initialBucketEnd, rollup.BucketSize, rollup.BucketAlignment);
    }

    private async Task EnsureCrateProvisionedAsync(ArchiveSnapshot snapshot)
    {
        using var activity = StreamDataDiagnostics.ActivitySource.StartActivity("archive.activate");
        activity?.SetTag("streamdata.archive.rtid", snapshot.RtId.ToString());
        activity?.SetTag("streamdata.archive.from_status", snapshot.Status.ToString());

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _repository.EnsureArchiveCreatedAsync(snapshot);

            stopwatch.Stop();
            StreamDataDiagnostics.ActivationDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new("archive", snapshot.RtId.ToString()),
                new("outcome", "success"));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            StreamDataDiagnostics.ActivationDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds,
                new("archive", snapshot.RtId.ToString()),
                new("outcome", "failed"));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex,
                "Failed to provision Crate table for archive {ArchiveRtId} (was {FromStatus})",
                snapshot.RtId, snapshot.Status);

            // Best-effort flip to Failed so the studio reflects reality. If this also fails the
            // outer exception still surfaces; the next reconciliation pass (T23) closes the loop.
            try
            {
                await _store.SetStatusAsync(snapshot.RtId, CkArchiveStatus.Failed);
                await _audit.RecordTransitionAsync(_tenantId, snapshot.RtId, snapshot.Status, CkArchiveStatus.Failed, ex.Message);

                StreamDataDiagnostics.StatusTransitions.Add(1,
                    new("archive", snapshot.RtId.ToString()),
                    new("from", snapshot.Status.ToString()),
                    new("to", CkArchiveStatus.Failed.ToString()));
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

    private async Task TransitionAsync(ArchiveSnapshot from, CkArchiveStatus toStatus)
    {
        await _store.SetStatusAsync(from.RtId, toStatus);
        await _audit.RecordTransitionAsync(_tenantId, from.RtId, from.Status, toStatus, reason: null);

        StreamDataDiagnostics.StatusTransitions.Add(1,
            new("archive", from.RtId.ToString()),
            new("from", from.Status.ToString()),
            new("to", toStatus.ToString()));

        _logger.LogInformation(
            "CkArchive {ArchiveRtId} transitioned {FromStatus} → {ToStatus}",
            from.RtId, from.Status, toStatus);
    }
}
