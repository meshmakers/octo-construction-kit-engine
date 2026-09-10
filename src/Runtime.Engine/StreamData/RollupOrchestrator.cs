using System;
using System.Collections.Generic;
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
/// <para>
/// Per-bucket ordering is <em>upsert rows first, advance watermark last</em>. On crash between
/// the two, the next tick re-aggregates the same bucket; the data store's upsert primitive
/// collapses duplicates via the natural key <c>(timestamp, rtId)</c> (concept §5). A consecutive-
/// failure counter is kept process-local and is not persisted across restarts — the
/// <c>Failed</c>-status escalation lands when the per-rollup failure tracker is moved into the
/// store (concept §8 follow-up).
/// </para>
/// <para>
/// Since AB#5157 a rollup declares several time-disjoint sources
/// (<see cref="RollupArchiveSnapshot.Sources"/>). Every source snapshot is loaded once per tick
/// and the source is then chosen <em>per bucket</em> via
/// <see cref="RollupArchiveSnapshot.SourceForBucket"/>: a bucket no span covers writes no row and
/// only advances the watermark; a bucket whose source is missing or not activated stops the tick
/// with the watermark unchanged, so data that source will deliver later is never skipped
/// (disabling a source of an activated rollup stays allowed — the rollup stalls with a warning).
/// The aggregation of one bucket never mixes two sources. The initial watermark seeded at
/// activation is unaffected: it derives from the clock alone, not from any source.
/// See <c>concept-multi-source-rollups.md</c> §5.
/// </para>
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
    private readonly bool _refreshOpenBucket;

    /// <summary>Constructs the orchestrator with tenant-scoped dependencies.</summary>
    public RollupOrchestrator(
        string tenantId,
        IArchiveRuntimeStore archiveStore,
        IRollupArchiveRuntimeStore rollupStore,
        IStreamDataRepository repository,
        IArchiveAuditTrail audit,
        ILogger<RollupOrchestrator> logger,
        int maxBucketsPerTick = DefaultMaxBucketsPerTick,
        Func<DateTime>? clock = null,
        bool refreshOpenBucket = false)
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
        _refreshOpenBucket = refreshOpenBucket;
    }

    /// <inheritdoc />
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        await foreach (var snapshot in _rollupStore.EnumerateAsync().WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // AB#4772 self-heal: entities seeded via ImportRt may lack the mandatory inherited
            // Columns attribute, which breaks the non-null GraphQL field for the whole archives
            // list. The tick sees every non-deleted rollup regardless of status (deliberately
            // before the Activated check, so Disabled/Created ones heal too); the store call is
            // a no-op once healed, and a failure must not stop the tick.
            if (!snapshot.HasPersistedColumns)
            {
                try
                {
                    if (await _rollupStore.TryPersistDerivedColumnsAsync(snapshot.RtId))
                    {
                        _logger.LogInformation(
                            "Rollup {RollupRtId}: persisted derived aggregate columns onto the entity (missing Columns attribute healed).",
                            snapshot.RtId);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Rollup {RollupRtId}: failed to persist derived aggregate columns; continuing tick.",
                        snapshot.RtId);
                }
            }

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

        // AB#5157: activation rejects a rollup without sources, but the generic CK mutation can
        // still empty the list afterwards. Treating every bucket as a gap would silently advance
        // the watermark past data — stall instead, exactly like an unavailable source.
        if (rollup.Sources.Count == 0)
        {
            _logger.LogWarning(
                "Rollup {RollupRtId}: declares no source archive; skipping until a source is declared",
                rollup.RtId);
            return 0;
        }

        // AB#5157: every declared source is resolved once per tick; the bucket loop below picks
        // the one whose validity span contains the bucket. A source may be raw, time-range or
        // another rollup — the archive store serves all three views.
        var sources = await LoadSourcesAsync(rollup);

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

            // AB#5157: per-bucket source selection. Spans are disjoint and bucket-aligned, so a
            // bucket lies entirely within one source's span or within none.
            var reference = rollup.SourceForBucket(bucketStart, bucketEnd);
            if (reference is null)
            {
                // A gap no source covers: no row is written (the bucket stays empty), but the
                // watermark moves on so the rollup does not stall on a deliberately uncovered range.
                await _rollupStore.AdvanceWatermarkAsync(rollup.RtId, bucketEnd);

                _logger.LogDebug(
                    "Rollup {RollupRtId}: bucket [{BucketStart:O}, {BucketEnd:O}) lies in no source's validity span; watermark advanced without a row",
                    rollup.RtId, bucketStart, bucketEnd);

                watermark = bucketEnd;
                continue;
            }

            var source = ResolveActivatedSource(rollup, sources, reference, LogLevel.Warning, "stopping tick with watermark unchanged");
            if (source is null)
            {
                // Never skip past a bucket whose source will deliver its data later (a source that
                // was disabled, or a chained rollup that is not activated yet): stop here and retry
                // on the next tick. Buckets committed earlier in this tick stay committed.
                break;
            }

            var stopwatch = Stopwatch.StartNew();
            var rowsWritten = await _repository.AggregateBucketAsync(
                source, rollup, bucketStart, bucketEnd, cancellationToken);
            stopwatch.Stop();

            await _rollupStore.AdvanceWatermarkAsync(rollup.RtId, bucketEnd);
            await _audit.RecordRollupRunAsync(
                _tenantId, rollup.RtId, bucketStart, bucketEnd, rowsWritten, stopwatch.Elapsed);

            _logger.LogDebug(
                "Rollup {RollupRtId}: committed bucket [{BucketStart:O}, {BucketEnd:O}) from source {SourceArchiveRtId} — {Rows} rows in {ElapsedMs}ms",
                rollup.RtId, bucketStart, bucketEnd, reference.SourceArchiveRtId, rowsWritten, stopwatch.Elapsed.TotalMilliseconds);

            watermark = bucketEnd;
            committed++;
        }

        // AB#4306: keep the CURRENT open bucket fresh. The loop above only commits CLOSED buckets
        // (bucketEnd <= now - lag) and stops at the first not-yet-closed one, leaving `watermark` at
        // its start. When enabled, re-aggregate that one open bucket every tick as a provisional row
        // WITHOUT advancing the watermark — so a "this month / this year so far" total tracks new
        // source data instead of only materialising once the period closes. AggregateBucketAsync is
        // an idempotent generation-0 upsert (ON CONFLICT), so repeating it is safe, and the forward
        // loop finalises the bucket normally once it closes. Cheap on a cascaded ladder (the open
        // bucket reads only a handful of rows from the finer level below).
        if (_refreshOpenBucket)
        {
            var openEnd = BucketBoundary.NextBucketEnd(watermark, rollup.BucketAlignment, rollup.BucketSize, zone);
            // openEnd > now - lag ⇔ this is exactly the not-yet-finalised bucket the loop stopped at.
            // If the loop instead stopped on the per-tick cap (backlog) or on an unavailable source,
            // openEnd is still past-and-finalisable and we skip, letting the closed loop catch up
            // first. Respect freeze too.
            var isCurrentOpenBucket = openEnd > now - rollup.WatermarkLag;
            var isFrozen = rollup.FrozenUntil is { } frozenUntil && openEnd <= frozenUntil;
            if (isCurrentOpenBucket && !isFrozen)
            {
                // AB#5157: the open bucket resolves its source the same way as a closed one. With
                // no covering span (or an unavailable source) the provisional row is simply not
                // refreshed; the closed loop reports the unavailable source once the bucket closes.
                var openReference = rollup.SourceForBucket(watermark, openEnd);
                if (openReference is null)
                {
                    _logger.LogDebug(
                        "Rollup {RollupRtId}: open bucket [{Start:O}, {End:O}) lies in no source's validity span; refresh skipped",
                        rollup.RtId, watermark, openEnd);
                }
                else if (ResolveActivatedSource(rollup, sources, openReference, LogLevel.Debug, "open-bucket refresh skipped") is { } openSource)
                {
                    var rows = await _repository.AggregateBucketAsync(openSource, rollup, watermark, openEnd, cancellationToken);
                    _logger.LogDebug(
                        "Rollup {RollupRtId}: refreshed open bucket [{Start:O}, {End:O}) provisionally from source {SourceArchiveRtId} — {Rows} rows",
                        rollup.RtId, watermark, openEnd, openReference.SourceArchiveRtId, rows);
                }
            }
        }

        if (committed > 0)
        {
            _logger.LogInformation(
                "Rollup {RollupRtId}: tick committed {Buckets} bucket(s) for tenant {TenantId}",
                rollup.RtId, committed, _tenantId);
        }

        return committed;
    }

    /// <summary>
    /// Loads the archive view of every declared source exactly once (a source listed twice — which
    /// the validator rejects — still costs one lookup). A missing source is kept as <c>null</c> so
    /// the bucket loop can report it only when a bucket actually needs it.
    /// </summary>
    private async Task<Dictionary<OctoObjectId, ArchiveSnapshot?>> LoadSourcesAsync(RollupArchiveSnapshot rollup)
    {
        var sources = new Dictionary<OctoObjectId, ArchiveSnapshot?>(rollup.Sources.Count);
        foreach (var reference in rollup.Sources)
        {
            if (sources.ContainsKey(reference.SourceArchiveRtId))
            {
                continue;
            }

            sources[reference.SourceArchiveRtId] = await _archiveStore.GetAsync(reference.SourceArchiveRtId);
        }

        return sources;
    }

    /// <summary>
    /// Returns the activated archive snapshot behind <paramref name="reference"/>, or <c>null</c>
    /// — logged at <paramref name="level"/> with the caller's <paramref name="consequence"/> —
    /// when the source no longer exists or is not activated.
    /// </summary>
    private ArchiveSnapshot? ResolveActivatedSource(
        RollupArchiveSnapshot rollup,
        IReadOnlyDictionary<OctoObjectId, ArchiveSnapshot?> sources,
        RollupSourceReference reference,
        LogLevel level,
        string consequence)
    {
        if (!sources.TryGetValue(reference.SourceArchiveRtId, out var source) || source is null)
        {
            _logger.Log(level,
                "Rollup {RollupRtId}: source archive {SourceArchiveRtId} not found (deleted?); {Consequence}",
                rollup.RtId, reference.SourceArchiveRtId, consequence);
            return null;
        }

        if (source.Status != CkArchiveStatus.Activated)
        {
            _logger.Log(level,
                "Rollup {RollupRtId}: source archive {SourceArchiveRtId} is {SourceStatus}; {Consequence} until it is activated",
                rollup.RtId, reference.SourceArchiveRtId, source.Status, consequence);
            return null;
        }

        return source;
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
