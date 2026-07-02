using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IRollupOrchestrator"/> implementation. Drives bucketed aggregation for
/// every <see cref="CkArchiveStatus.Activated"/> rollup in the tenant: for each rollup the
/// service walks the closed-bucket range <c>[watermark, now - watermarkLag)</c>, delegates one
/// SQL upsert per bucket to <see cref="IStreamDataRepository.AggregateBucketAsync"/>, advances
/// the watermark, and emits an audit event. Rollup-archives concept §5, §8, §11.
/// </summary>
/// <remarks>
/// Per-bucket ordering is <em>upsert rows first, advance watermark last</em>. On crash between
/// the two, the next tick re-aggregates the same bucket; the data store's upsert primitive
/// collapses duplicates via the natural key <c>(timestamp, rtId)</c> (concept §5). A consecutive-
/// failure counter is kept process-local and is not persisted across restarts — the
/// <c>Failed</c>-status escalation lands when the per-rollup failure tracker is moved into the
/// store (concept §8 follow-up).
/// </remarks>
public sealed class RollupOrchestrator : IRollupOrchestrator
{
    /// <summary>Default upper bound on the number of buckets processed for a single rollup per tick.</summary>
    public const int DefaultMaxBucketsPerTick = 60;

    private readonly string _tenantId;
    private readonly IArchiveRuntimeStore _archiveStore;
    private readonly IRollupArchiveRuntimeStore _rollupStore;
    private readonly IStreamDataRepository _repository;
    private readonly IArchiveAuditTrail _audit;
    private readonly ILogger<RollupOrchestrator> _logger;
    private readonly int _maxBucketsPerTick;
    private readonly Func<DateTime> _clock;

    /// <summary>Constructs the orchestrator with tenant-scoped dependencies.</summary>
    public RollupOrchestrator(
        string tenantId,
        IArchiveRuntimeStore archiveStore,
        IRollupArchiveRuntimeStore rollupStore,
        IStreamDataRepository repository,
        IArchiveAuditTrail audit,
        ILogger<RollupOrchestrator> logger,
        int maxBucketsPerTick = DefaultMaxBucketsPerTick,
        Func<DateTime>? clock = null)
    {
        if (maxBucketsPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBucketsPerTick), maxBucketsPerTick,
                "Must be positive.");
        }

        _tenantId = tenantId;
        _archiveStore = archiveStore;
        _rollupStore = rollupStore;
        _repository = repository;
        _audit = audit;
        _logger = logger;
        _maxBucketsPerTick = maxBucketsPerTick;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        await foreach (var snapshot in _rollupStore.EnumerateAsync().WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshot.Status != CkArchiveStatus.Activated)
            {
                continue;
            }

            try
            {
                total += await ProcessRollupSnapshotAsync(snapshot, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One rollup failing must not stop the tick — log + continue. Per-rollup failure
                // tracking → CkArchiveStatus.Failed escalation lives in the lifecycle service
                // (concept §8) and is invoked separately.
                _logger.LogError(ex,
                    "Rollup orchestrator: failed to process rollup {RollupRtId} for tenant {TenantId}",
                    snapshot.RtId, _tenantId);
            }
        }

        return total;
    }

    /// <inheritdoc />
    public async Task<int> ProcessRollupAsync(OctoObjectId rollupRtId, CancellationToken cancellationToken)
    {
        var snapshot = await LoadRollupAsync(rollupRtId);
        if (snapshot.Status != CkArchiveStatus.Activated)
        {
            _logger.LogDebug(
                "Rollup {RollupRtId} is in status {Status}; skipping orchestration",
                rollupRtId, snapshot.Status);
            return 0;
        }
        return await ProcessRollupSnapshotAsync(snapshot, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RewindWatermarkAsync(OctoObjectId rollupRtId, DateTime toBucketEnd)
    {
        var snapshot = await LoadRollupAsync(rollupRtId);

        // Truncate down to the bucket boundary so the next tick starts cleanly. The alignment-
        // aware helper handles FixedSize (legacy modulo arithmetic) and calendar variants
        // (snap to start of period containing the target) uniformly. The reference time-zone
        // (AB#4300 / O6) makes calendar boundaries land on local wall-clock midnight, not UTC.
        var rewindZone = BucketBoundary.ResolveZone(snapshot.ReferenceTimeZone);
        toBucketEnd = BucketBoundary.AlignDown(toBucketEnd, snapshot.BucketAlignment, snapshot.BucketSize, rewindZone);

        await _rollupStore.AdvanceWatermarkAsync(rollupRtId, toBucketEnd, allowRewind: true);

        _logger.LogWarning(
            "Rollup {RollupRtId} watermark rewound to {Watermark:O}. Re-aggregation pending on next tick.",
            rollupRtId, toBucketEnd);
    }

    private async Task<int> ProcessRollupSnapshotAsync(
        RollupArchiveSnapshot rollup, CancellationToken cancellationToken)
    {
        if (rollup.BucketSize <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Rollup {RollupRtId}: BucketSize is non-positive ({BucketSize}); skipping",
                rollup.RtId, rollup.BucketSize);
            return 0;
        }

        if (rollup.LastAggregatedBucketEnd is null)
        {
            _logger.LogDebug(
                "Rollup {RollupRtId}: watermark is null; initial-watermark logic is deferred to activation. Skipping until set.",
                rollup.RtId);
            return 0;
        }

        var source = await _archiveStore.GetAsync(rollup.SourceArchiveRtId);
        if (source is null)
        {
            _logger.LogWarning(
                "Rollup {RollupRtId}: source archive {SourceArchiveRtId} not found (deleted?); skipping",
                rollup.RtId, rollup.SourceArchiveRtId);
            return 0;
        }

        if (source.Status != CkArchiveStatus.Activated)
        {
            _logger.LogDebug(
                "Rollup {RollupRtId}: source archive {SourceArchiveRtId} is {SourceStatus}; skipping until activated",
                rollup.RtId, rollup.SourceArchiveRtId, source.Status);
            return 0;
        }

        var now = _clock();
        var watermark = rollup.LastAggregatedBucketEnd.Value;
        var committed = 0;
        // Reference time-zone for calendar bucket boundaries (AB#4300 / O6); null ⇒ UTC.
        var zone = BucketBoundary.ResolveZone(rollup.ReferenceTimeZone);

        for (var i = 0; i < _maxBucketsPerTick; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bucketStart = watermark;
            var bucketEnd = BucketBoundary.NextBucketEnd(bucketStart, rollup.BucketAlignment, rollup.BucketSize, zone);

            // Wait until the bucket is fully past + lag so late-arriving inserts are captured.
            if (bucketEnd > now - rollup.WatermarkLag)
            {
                break;
            }

            // Frozen ranges are preserved as-is (concept §6) — skip the bucket but still advance
            // the watermark so the orchestrator catches up to the frozen-until boundary.
            if (rollup.FrozenUntil is { } frozenUntil && bucketEnd <= frozenUntil)
            {
                watermark = bucketEnd;
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var rowsWritten = await _repository.AggregateBucketAsync(
                source, rollup, bucketStart, bucketEnd, cancellationToken);
            stopwatch.Stop();

            await _rollupStore.AdvanceWatermarkAsync(rollup.RtId, bucketEnd);
            await _audit.RecordRollupRunAsync(
                _tenantId, rollup.RtId, bucketStart, bucketEnd, rowsWritten, stopwatch.Elapsed);

            _logger.LogDebug(
                "Rollup {RollupRtId}: committed bucket [{BucketStart:O}, {BucketEnd:O}) — {Rows} rows in {ElapsedMs}ms",
                rollup.RtId, bucketStart, bucketEnd, rowsWritten, stopwatch.Elapsed.TotalMilliseconds);

            watermark = bucketEnd;
            committed++;
        }

        if (committed > 0)
        {
            _logger.LogInformation(
                "Rollup {RollupRtId}: tick committed {Buckets} bucket(s) for tenant {TenantId}",
                rollup.RtId, committed, _tenantId);
        }

        return committed;
    }

    private async Task<RollupArchiveSnapshot> LoadRollupAsync(OctoObjectId rollupRtId)
    {
        var snapshot = await _rollupStore.GetAsync(rollupRtId);
        if (snapshot is null)
        {
            throw new ArchiveNotFoundException(rollupRtId);
        }
        return snapshot;
    }
}
