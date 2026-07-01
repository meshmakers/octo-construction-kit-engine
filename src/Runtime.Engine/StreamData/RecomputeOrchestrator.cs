using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Coordinates optimistic rollup recompute (AB#4184). Owns the recompute job lifecycle, the
/// coalesce policy, observability, and dependency propagation; delegates the actual staging-compute
/// + atomic swap to <see cref="IArchiveRecomputeExecutor"/> (implemented over CrateDB in Phase 3c).
/// One instance per tenant, mirroring <see cref="RollupOrchestrator"/>.
/// </summary>
/// <remarks>
/// Propagation is one level per recompute: a successful recompute of an archive enqueues pending
/// ranges onto its <em>direct</em> dependents, and each of those, when it recomputes, propagates to
/// its own dependents. A multi-level rollup-of-rollup chain therefore converges over successive
/// ticks — the same eventual-consistency model the forward watermark orchestrator uses.
/// </remarks>
public sealed class RecomputeOrchestrator : IRecomputeOrchestrator
{
    /// <summary>
    /// Default maximum number of buckets a single executor sub-run recomputes (AB#4283). Sized so the
    /// staging→live copy and superseded-row sweep for one chunk stay comfortably under the CrateDB
    /// per-statement / Polly timeout (30s): a 1-day hourly range (24 buckets) recomputes in ~300ms, so
    /// 2000 buckets is well inside budget while keeping the number of chunks (and pointer flips) for a
    /// decade-long backfill modest (~44 chunks for 10y hourly). Overridable via the constructor.
    /// </summary>
    public const int DefaultMaxBucketsPerChunk = 2000;

    private readonly string _tenantId;
    private readonly IArchiveRuntimeStore _archiveStore;
    private readonly IRollupArchiveRuntimeStore _rollupStore;
    private readonly IRollupDependencyGraph _dependencyGraph;
    private readonly IArchiveRecomputeStateStore _stateStore;
    private readonly IRecomputeJobStore _jobStore;
    private readonly IArchiveRecomputeExecutor _executor;
    private readonly IStreamDataRepository _streamData;
    private readonly IArchiveAuditTrail _audit;
    private readonly ILogger<RecomputeOrchestrator> _logger;
    private readonly Func<DateTime> _clock;
    private readonly int _maxBucketsPerChunk;

    /// <summary>Constructs the orchestrator for one tenant.</summary>
    /// <remarks>
    /// <c>maxBucketsPerChunk</c> (AB#4283) caps how many buckets a single executor sub-run recomputes.
    /// A large <c>[from, to)</c> recompute is split into contiguous chunks of at most this many buckets
    /// so no single CrateDB statement exceeds the per-statement timeout. Defaults to
    /// <see cref="DefaultMaxBucketsPerChunk"/>. Must be positive.
    /// </remarks>
    public RecomputeOrchestrator(
        string tenantId,
        IArchiveRuntimeStore archiveStore,
        IRollupArchiveRuntimeStore rollupStore,
        IRollupDependencyGraph dependencyGraph,
        IArchiveRecomputeStateStore stateStore,
        IRecomputeJobStore jobStore,
        IArchiveRecomputeExecutor executor,
        IStreamDataRepository streamData,
        IArchiveAuditTrail audit,
        ILogger<RecomputeOrchestrator> logger,
        Func<DateTime> clock,
        int maxBucketsPerChunk = DefaultMaxBucketsPerChunk)
    {
        if (maxBucketsPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBucketsPerChunk), maxBucketsPerChunk, "Chunk size must be positive.");
        }

        _tenantId = tenantId;
        _archiveStore = archiveStore;
        _rollupStore = rollupStore;
        _dependencyGraph = dependencyGraph;
        _stateStore = stateStore;
        _jobStore = jobStore;
        _executor = executor;
        _streamData = streamData;
        _audit = audit;
        _logger = logger;
        _clock = clock;
        _maxBucketsPerChunk = maxBucketsPerChunk;
    }

    /// <summary>
    /// One periodic tick: fan out every source's dirty windows onto its dependents (Information A →
    /// B), then drain each activated rollup's pending recompute ranges (coalesced into disjoint
    /// intervals). Returns the number of recompute runs executed.
    /// </summary>
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        await foreach (var archive in _archiveStore.EnumerateAsync().WithCancellation(cancellationToken))
        {
            await PropagateDirtyWindowsAsync(archive.RtId, cancellationToken);
        }

        // Snapshot the rollup list up front — recompute mutates pending-range state as it runs.
        var rollups = new List<RollupArchiveSnapshot>();
        await foreach (var rollup in _rollupStore.EnumerateAsync().WithCancellation(cancellationToken))
        {
            rollups.Add(rollup);
        }

        var recomputeCount = 0;
        foreach (var rollup in rollups)
        {
            if (rollup.Status != CkArchiveStatus.Activated)
            {
                continue;
            }

            var pending = await _stateStore.GetPendingRecomputeRangesAsync(rollup.RtId);
            if (pending.Count == 0)
            {
                continue;
            }

            var merged = RecomputePlanner.MergeIntervals(pending.Select(r => (r.RangeStart, r.RangeEnd)));

            // Clear the consumed work first; a failed interval is re-enqueued below so it retries on
            // the next tick, and chain propagation targets *other* archives' lists, not this one.
            await _stateStore.ClearPendingRecomputeRangesAsync(rollup.RtId);

            foreach (var (start, end) in merged)
            {
                var job = await RecomputeArchiveAsync(
                    rollup.RtId, start, end, rtIdScope: null, RecomputeTrigger.Periodic, cancellationToken);

                if (job.State == RecomputeJobState.Failed)
                {
                    await _stateStore.EnqueueRecomputeRangesAsync(rollup.RtId,
                        new[] { new ArchiveRecomputeRange(rollup.RtId, start, end, null, _clock()) });
                }
                else
                {
                    recomputeCount++;
                }
            }
        }

        return recomputeCount;
    }

    /// <summary>
    /// Information A → B: turns a source archive's retroactive dirty windows into pending recompute
    /// ranges on its direct dependents, then clears the windows. Append-style changes are ignored
    /// (the forward watermark orchestrator already covers them).
    /// </summary>
    public async Task PropagateDirtyWindowsAsync(OctoObjectId sourceArchiveRtId, CancellationToken cancellationToken)
    {
        var windows = await _stateStore.GetDirtyWindowsAsync(sourceArchiveRtId);
        if (windows.Count == 0)
        {
            return;
        }

        foreach (var window in windows)
        {
            if (window.ChangeKind != RecomputeChangeKind.RetroactiveModify)
            {
                continue;
            }

            await EnqueueOnDirectDependentsAsync(sourceArchiveRtId, window.WindowStart, window.WindowEnd, cancellationToken);
        }

        await _stateStore.ClearDirtyWindowsAsync(sourceArchiveRtId);
    }

    /// <summary>
    /// Executes (or coalesces) one recompute of a rollup over the bucket-aligned range
    /// <c>[from, to)</c>. If a job is already active for the rollup, the range is parked as pending
    /// work and a <see cref="RecomputeJobState.Coalesced"/> record is returned. Otherwise a job runs
    /// through Running → Completed/Failed with full observability, and on success the rollup's direct
    /// dependents are marked stale for the same range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chunking (AB#4283).</b> A large <c>[from, to)</c> is split into contiguous, bucket-aligned
    /// sub-ranges of at most <c>maxBucketsPerChunk</c> buckets and the executor is called once per
    /// chunk, so no single CrateDB statement (per-bucket aggregate, staging→live copy, sweep) exceeds
    /// the per-statement / Polly timeout — a decade-long backfill that previously failed with "the
    /// operation was canceled" now completes as a sequence of bounded, individually-atomic chunk
    /// swaps. <c>rowsProcessed</c> / <c>windowsProcessed</c> accumulate across chunks into the single
    /// <c>RecomputeJob</c>, whose progress is persisted after each chunk so a long backfill is
    /// observable while it runs.
    /// </para>
    /// <para>
    /// <b>Partial-progress semantics.</b> Each chunk's staging→swap is atomic on its own sub-range
    /// (its own per-window generation pointer). If a chunk fails, the job fails with that chunk's real
    /// error, but every chunk that already committed stays committed — the recompute is
    /// resumable-by-retry rather than all-or-nothing. A subsequent recompute of the same range simply
    /// re-processes and re-commits the chunks (idempotent under the generation pointer).
    /// </para>
    /// </remarks>
    public async Task<RecomputeJobSnapshot> RecomputeArchiveAsync(
        OctoObjectId rollupRtId,
        DateTime from,
        DateTime to,
        OctoObjectId? rtIdScope,
        RecomputeTrigger trigger,
        CancellationToken cancellationToken)
    {
        var now = _clock();

        var active = await _jobStore.GetActiveForArchiveAsync(rollupRtId);
        if (active is not null)
        {
            await _stateStore.EnqueueRecomputeRangesAsync(rollupRtId,
                new[] { new ArchiveRecomputeRange(rollupRtId, from, to, rtIdScope, now) });

            _logger.LogInformation(
                "Recompute of {RollupRtId} range [{From:O},{To:O}) coalesced into active job {ActiveJob}",
                rollupRtId, from, to, active.RtId);

            return await PersistNewJobAsync(new RecomputeJobSnapshot(
                OctoObjectId.Empty, rollupRtId, RecomputeJobState.Coalesced, trigger,
                from, to, rtIdScope, null, null, now, now, 0, null, null));
        }

        var rollup = await _rollupStore.GetAsync(rollupRtId);
        if (rollup is null)
        {
            return await FailImmediatelyAsync(rollupRtId, from, to, rtIdScope, trigger, now,
                "Archive is not a rollup (or has been deleted).");
        }

        var source = await _archiveStore.GetAsync(rollup.SourceArchiveRtId);
        if (source is null)
        {
            return await FailImmediatelyAsync(rollupRtId, from, to, rtIdScope, trigger, now,
                $"Source archive {rollup.SourceArchiveRtId} not found.");
        }

        var startedAt = now;
        var job = await PersistNewJobAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, rollupRtId, RecomputeJobState.Running, trigger,
            from, to, rtIdScope, null, null, startedAt, null, null, null, null));

        await _stateStore.MarkRecomputeStartedAsync(rollupRtId, startedAt);

        try
        {
            // AB#4283: split the (possibly decade-long) range into bucket-aligned chunks so every
            // executor sub-run keeps its CrateDB statements under the per-statement timeout. Each chunk
            // swap is atomic on its own sub-range; totals accumulate into this one job.
            var chunks = RecomputePlanner.PlanChunks(
                from, to, rollup.BucketAlignment, rollup.BucketSize, _maxBucketsPerChunk);

            if (chunks.Count > 1)
            {
                _logger.LogInformation(
                    "Recompute of rollup {RollupRtId} range [{From:O},{To:O}) split into {ChunkCount} chunks of ≤{MaxBuckets} buckets.",
                    rollupRtId, from, to, chunks.Count, _maxBucketsPerChunk);
            }

            var totalRows = 0;
            var totalWindows = 0;
            var chunkIndex = 0;
            foreach (var (chunkStart, chunkEnd) in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkResult = await _executor.ExecuteAsync(
                    source, rollup, chunkStart, chunkEnd, rtIdScope, cancellationToken);
                totalRows += chunkResult.RowsProcessed;
                totalWindows += chunkResult.WindowsProcessed;
                chunkIndex++;

                // Persist running totals after each chunk so a long backfill is observable while it
                // runs. State stays Running; the terminal Completed write happens once below.
                job = job with { RowsProcessed = totalRows, WindowsProcessed = totalWindows };
                await _jobStore.UpdateAsync(job);

                if (chunks.Count > 1)
                {
                    _logger.LogInformation(
                        "Recompute of rollup {RollupRtId}: chunk {ChunkIndex}/{ChunkCount} [{ChunkStart:O},{ChunkEnd:O}) " +
                        "done ({ChunkRows} rows / {ChunkWindows} windows; running totals {TotalRows}/{TotalWindows}).",
                        rollupRtId, chunkIndex, chunks.Count, chunkStart, chunkEnd,
                        chunkResult.RowsProcessed, chunkResult.WindowsProcessed, totalRows, totalWindows);
                }
            }

            var finishedAt = _clock();
            var elapsed = finishedAt - startedAt;

            var completed = job with
            {
                State = RecomputeJobState.Completed,
                RowsProcessed = totalRows,
                WindowsProcessed = totalWindows,
                FinishedAt = finishedAt,
                DurationMs = (int)elapsed.TotalMilliseconds,
            };
            await _jobStore.UpdateAsync(completed);
            await _stateStore.MarkRecomputeSucceededAsync(rollupRtId, finishedAt);
            await _audit.RecordRecomputeRunAsync(
                _tenantId, rollupRtId, from, to, totalRows, totalWindows, elapsed);

            // Chain: this rollup's values changed in [from, to) → its direct dependents are stale.
            await EnqueueOnDirectDependentsAsync(rollupRtId, from, to, cancellationToken);

            return completed;
        }
        catch (Exception ex)
        {
            var finishedAt = _clock();
            var failed = job with
            {
                State = RecomputeJobState.Failed,
                FinishedAt = finishedAt,
                DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
                ErrorReason = ex.Message,
            };
            await _jobStore.UpdateAsync(failed);
            await _stateStore.MarkRecomputeFailedAsync(rollupRtId, finishedAt, ex.Message);
            await _audit.RecordRecomputeFailureAsync(_tenantId, rollupRtId, from, to, ex.Message);

            _logger.LogError(ex,
                "Recompute of rollup {RollupRtId} range [{From:O},{To:O}) failed", rollupRtId, from, to);

            return failed;
        }
    }

    /// <summary>
    /// Backfill (AB#4269): populate / reset a rollup over the entire history of its source archive.
    /// Resolves the source archive's earliest stored timestamp, snaps it down to the rollup's bucket
    /// boundary, and recomputes <c>[sourceMin, now)</c> through the optimistic recompute path. An
    /// empty source archive is a no-op (returns <c>null</c>); a non-rollup target produces a failed
    /// job, exactly as <see cref="RecomputeArchiveAsync"/> would.
    /// </summary>
    public async Task<RecomputeJobSnapshot?> BackfillRollupFromSourceAsync(
        OctoObjectId rollupRtId, CancellationToken cancellationToken)
    {
        var rollup = await _rollupStore.GetAsync(rollupRtId);
        if (rollup is null)
        {
            // Not a rollup (or deleted): delegate to the recompute path so the caller gets the same
            // failed-job shape recomputeArchive would return for a non-rollup target.
            var nowForFailure = _clock();
            return await RecomputeArchiveAsync(
                rollupRtId, nowForFailure, nowForFailure, rtIdScope: null, RecomputeTrigger.Manual, cancellationToken);
        }

        var sourceMin = await _streamData.GetArchiveMinTimestampAsync(rollup.SourceArchiveRtId, cancellationToken);
        if (sourceMin is null)
        {
            _logger.LogInformation(
                "Backfill of rollup {RollupRtId}: source archive {SourceRtId} holds no data — nothing to recompute (no-op).",
                rollupRtId, rollup.SourceArchiveRtId);
            return null;
        }

        var now = _clock();

        // Snap the source's earliest timestamp down to the rollup's bucket boundary so the recompute
        // starts on a clean bucket-start; recompute the whole history [from, now).
        var (from, _) = RecomputePlanner.AlignRangeToBuckets(
            sourceMin.Value, sourceMin.Value, rollup.BucketAlignment, rollup.BucketSize);

        if (now <= from)
        {
            _logger.LogInformation(
                "Backfill of rollup {RollupRtId}: aligned source start {From:O} is not before now {Now:O} — nothing to recompute (no-op).",
                rollupRtId, from, now);
            return null;
        }

        _logger.LogInformation(
            "Backfill of rollup {RollupRtId}: recomputing entire history [{From:O}, {Now:O}) from source {SourceRtId} (earliest source ts {SourceMin:O}).",
            rollupRtId, from, now, rollup.SourceArchiveRtId, sourceMin.Value);

        return await RecomputeArchiveAsync(
            rollupRtId, from, now, rtIdScope: null, RecomputeTrigger.Manual, cancellationToken);
    }

    private async Task EnqueueOnDirectDependentsAsync(
        OctoObjectId sourceRtId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var dependents = await _dependencyGraph.GetTransitiveDependentsAsync(sourceRtId);
        foreach (var dependent in dependents)
        {
            // Direct children only; deeper levels are reached when each child recomputes and
            // propagates to its own dependents. Re-deriving the range per dependent honours that
            // dependent's own bucket alignment.
            if (dependent.SourceArchiveRtId != sourceRtId)
            {
                continue;
            }

            var (start, end) = RecomputePlanner.AlignRangeToBuckets(
                from, to, dependent.BucketAlignment, dependent.BucketSize);

            await _stateStore.EnqueueRecomputeRangesAsync(dependent.RtId,
                new[] { new ArchiveRecomputeRange(dependent.RtId, start, end, null, _clock()) });
        }
    }

    private async Task<RecomputeJobSnapshot> PersistNewJobAsync(RecomputeJobSnapshot job)
    {
        var rtId = await _jobStore.CreateAsync(job);
        return job with { RtId = rtId };
    }

    private async Task<RecomputeJobSnapshot> FailImmediatelyAsync(
        OctoObjectId rollupRtId, DateTime from, DateTime to, OctoObjectId? rtIdScope,
        RecomputeTrigger trigger, DateTime now, string reason)
    {
        var failed = await PersistNewJobAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, rollupRtId, RecomputeJobState.Failed, trigger,
            from, to, rtIdScope, null, null, now, now, 0, reason, null));

        await _audit.RecordRecomputeFailureAsync(_tenantId, rollupRtId, from, to, reason);
        _logger.LogWarning("Recompute of {RollupRtId} not started: {Reason}", rollupRtId, reason);
        return failed;
    }
}
