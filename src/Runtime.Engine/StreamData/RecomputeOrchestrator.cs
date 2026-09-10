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
/// <para>
/// Propagation is one level per recompute: a successful recompute of an archive enqueues pending
/// ranges onto its <em>direct</em> dependents, and each of those, when it recomputes, propagates to
/// its own dependents. A multi-level rollup-of-rollup chain therefore converges over successive
/// ticks — the same eventual-consistency model the forward watermark orchestrator uses.
/// </para>
/// <para>
/// Since AB#5157 a rollup declares several time-disjoint sources
/// (<see cref="RollupArchiveSnapshot.Sources"/>), so the dependency graph is a multi-parent DAG.
/// A dirty range coming from one source is clipped to that source's validity span (on both ends)
/// before it is enqueued on a dependent; a recompute of <c>[from, to)</c> is split into one
/// segment per source along the spans and the executor runs once per segment — a segment never
/// mixes two sources; and the backfill start is the earliest in-span stored timestamp over all
/// sources. See <c>concept-multi-source-rollups.md</c> §6.
/// </para>
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

    /// <summary>
    /// Default number of attempts per chunk (AB#4278) before the chunk — and therefore the job —
    /// fails. One initial try plus three retries. Each chunk is idempotent under the per-window
    /// generation pointer, so replaying it after a dropped CrateDB connection ("Exception while
    /// reading from stream" / <c>EndOfStreamException</c>) re-commits the same rows without
    /// duplication. This guarantees a single intermittent connection drop can never abort a
    /// decade-long backfill.
    /// </summary>
    public const int DefaultMaxChunkAttempts = 4;

    /// <summary>
    /// Default base delay for the per-chunk retry backoff (AB#4278). Exponential: attempt <c>n</c>
    /// waits <c>base * 2^(n-1)</c> before retrying, giving a struggling / re-electing CrateDB cluster
    /// time to recover between attempts.
    /// </summary>
    public static readonly TimeSpan DefaultChunkRetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Default number of times the drain re-enqueues a failing recompute range before giving up on it
    /// (AB#5189). The per-chunk retry (<see cref="DefaultMaxChunkAttempts"/>) covers a dropped
    /// connection inside one run; this one covers a range that fails run after run. Without it a
    /// permanently broken rollup — a source that cannot serve an aggregation path, say — consumes a
    /// recompute run and a generation on every single tick, forever. Five attempts spread over the
    /// backoff below give a genuinely transient outage well over an hour to clear.
    /// </summary>
    public const int DefaultMaxRangeAttempts = 5;

    /// <summary>
    /// Default base delay for the per-range retry backoff (AB#5189). Exponential: after the
    /// <c>n</c>-th failure the range is held back for <c>base * 2^(n-1)</c>, so the five attempts run
    /// at roughly +1, +2, +4 and +8 tick-minutes rather than hammering the storage layer once a
    /// minute. Capped at <see cref="MaxRangeRetryDelay"/>.
    /// </summary>
    public static readonly TimeSpan DefaultRangeRetryBaseDelay = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound for the per-range backoff, so a large attempt count cannot overflow.</summary>
    public static readonly TimeSpan MaxRangeRetryDelay = TimeSpan.FromHours(1);

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
    private readonly int _maxChunkAttempts;
    private readonly TimeSpan _chunkRetryBaseDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IArchiveCoverageInvalidator? _coverageInvalidator;
    private readonly int _maxRangeAttempts;
    private readonly TimeSpan _rangeRetryBaseDelay;

    /// <summary>Constructs the orchestrator for one tenant.</summary>
    /// <remarks>
    /// <para><c>maxBucketsPerChunk</c> (AB#4283) caps how many buckets a single executor sub-run
    /// recomputes. A large <c>[from, to)</c> recompute is split into contiguous chunks of at most this
    /// many buckets so no single CrateDB statement exceeds the per-statement timeout. Defaults to
    /// <see cref="DefaultMaxBucketsPerChunk"/>. Must be positive.</para>
    /// <para><c>maxChunkAttempts</c> / <c>chunkRetryBaseDelay</c> (AB#4278) bound the per-chunk retry
    /// that makes a decade-long backfill survive intermittent CrateDB connection drops. Must be ≥ 1.
    /// <c>delay</c> is the (injectable, for tests) backoff sleep; defaults to
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</para>
    /// <para><c>coverageInvalidator</c> (AB#5157) is optional; when supplied, a completed recompute
    /// job drops the recomputed rollup's memoised coverage so the next coverage query re-measures
    /// instead of waiting out the cache TTL.</para>
    /// <para><c>maxRangeAttempts</c> / <c>rangeRetryBaseDelay</c> (AB#5189) bound how often the drain
    /// re-enqueues a range that keeps failing, and how long it is held back between attempts. Must be
    /// ≥ 1.</para>
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
        int maxBucketsPerChunk = DefaultMaxBucketsPerChunk,
        int maxChunkAttempts = DefaultMaxChunkAttempts,
        TimeSpan? chunkRetryBaseDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IArchiveCoverageInvalidator? coverageInvalidator = null,
        int maxRangeAttempts = DefaultMaxRangeAttempts,
        TimeSpan? rangeRetryBaseDelay = null)
    {
        if (maxBucketsPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBucketsPerChunk), maxBucketsPerChunk, "Chunk size must be positive.");
        }

        if (maxChunkAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxChunkAttempts), maxChunkAttempts, "Chunk attempts must be positive.");
        }

        if (maxRangeAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRangeAttempts), maxRangeAttempts, "Range attempts must be positive.");
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
        _maxChunkAttempts = maxChunkAttempts;
        _chunkRetryBaseDelay = chunkRetryBaseDelay ?? DefaultChunkRetryBaseDelay;
        _delay = delay ?? Task.Delay;
        _coverageInvalidator = coverageInvalidator;
        _maxRangeAttempts = maxRangeAttempts;
        _rangeRetryBaseDelay = rangeRetryBaseDelay ?? DefaultRangeRetryBaseDelay;
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

            // AB#5189: a range that has already failed is held back until its backoff has elapsed.
            // Only the due ones are consumed this tick; the rest stay in the work list untouched.
            var tickNow = _clock();
            var due = pending.Where(r => r.IsDueAt(tickNow)).ToList();
            if (due.Count == 0)
            {
                continue;
            }

            var heldBack = pending.Where(r => !r.IsDueAt(tickNow)).ToList();

            // AB#4286: a durable background backfill / manual recompute pre-creates a Pending job for
            // pollability (the client that queued the work is handed that job id). Adopt it here so the
            // client polls one job id from Pending → Completed, instead of the pre-created Pending job
            // lingering while a throwaway Periodic job actually runs the range.
            var active = await _jobStore.GetActiveForArchiveAsync(rollup.RtId);
            var adoptJob = active is { State: RecomputeJobState.Pending } ? active : null;

            var merged = RecomputePlanner.MergeIntervals(due.Select(r => (r.RangeStart, r.RangeEnd)));

            // Consume the due work in a single write that keeps the backed-off ranges (AB#5189).
            // Clear-then-enqueue would lose every held-back obligation if the process died in
            // between — the exact class of silent work loss this item is about.
            await _stateStore.ReplacePendingRecomputeRangesAsync(rollup.RtId, heldBack);

            foreach (var (start, end) in merged)
            {
                // The first merged interval adopts the pre-created Pending job (keeping its trigger,
                // e.g. Manual for a backfill); any further disjoint intervals run as fresh Periodic
                // jobs. Only one pre-created job exists per rollup.
                var trigger = adoptJob?.Trigger ?? RecomputeTrigger.Periodic;
                var outcome = await RecomputeArchiveInternalAsync(
                    rollup.RtId, start, end, rtIdScope: null, trigger, cancellationToken, adoptJob);
                adoptJob = null;

                if (outcome.Job.State != RecomputeJobState.Failed)
                {
                    recomputeCount++;
                }

                // AB#5189: re-enqueue only what did NOT run, and carry the attempt count of the
                // obligations this interval was merged from. The committed prefix is not repeated —
                // before this, a permanently failing range re-ran every committed chunk on every
                // tick, burning a generation each time.
                if (outcome.UnfinishedFrom is not { } unfinishedFrom)
                {
                    continue;
                }

                var attempts = MaxAttemptsCovering(due, start, end) + 1;
                var reason = outcome.Job.ErrorReason;

                if (attempts >= _maxRangeAttempts)
                {
                    _logger.LogError(
                        "Recompute of rollup {RollupRtId}: range [{Start:O},{End:O}) failed {Attempts} times " +
                        "and is given up on — [{UnfinishedFrom:O},{End:O}) stays un-recomputed until the " +
                        "cause is fixed and a recompute is triggered again. Last error: {Reason}",
                        rollup.RtId, start, end, attempts, unfinishedFrom, end, reason);
                    continue;
                }

                var backoff = RangeRetryDelay(attempts);
                _logger.LogWarning(
                    "Recompute of rollup {RollupRtId}: [{UnfinishedFrom:O},{End:O}) still outstanding after " +
                    "attempt {Attempts}/{MaxAttempts}; retrying after {Backoff}. Last error: {Reason}",
                    rollup.RtId, unfinishedFrom, end, attempts, _maxRangeAttempts, backoff, reason);

                await _stateStore.EnqueueRecomputeRangesAsync(rollup.RtId,
                    new[]
                    {
                        new ArchiveRecomputeRange(
                            rollup.RtId, unfinishedFrom, end, null, tickNow,
                            attempts, tickNow + backoff, reason),
                    });
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

        // AB#4196 belt-and-suspenders: the retroactive-write detector already floors an automatic
        // dirty window at the source's bounded-retro-reach cap, so a freshly-detected window is
        // in-reach by construction. Re-apply the source's per-archive cap here too, so a dirty window
        // recorded before the cap existed (legacy / pre-1.6.8) — or from any future non-detector
        // source — can still never drag an unbounded automatic recompute. The fleet-wide hard limit is
        // enforced authoritatively at detection; here we only have the per-archive value.
        var source = await _archiveStore.GetAsync(sourceArchiveRtId);
        var maxRetroReach = source?.MaxRetroactiveReachMs is { } ms && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : (TimeSpan?)null;

        foreach (var window in windows)
        {
            if (window.ChangeKind != RecomputeChangeKind.RetroactiveModify)
            {
                continue;
            }

            await EnqueueOnDirectDependentsAsync(
                sourceArchiveRtId, window.WindowStart, window.WindowEnd, cancellationToken, maxRetroReach);
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
    /// <para>
    /// <b>Connection stability (AB#4278).</b> Each chunk's executor call is additionally wrapped in a
    /// bounded per-chunk retry: an intermittent CrateDB connection drop ("Exception while reading from
    /// stream" / <c>EndOfStreamException</c>) mid-backfill retries the whole (idempotent) chunk with
    /// exponential backoff instead of aborting the job, so a decade-long backfill completes reliably
    /// over an unstable connection. Only a transient connection-class failure is retried; a
    /// deterministic error fails the chunk immediately.
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
        var outcome = await RecomputeArchiveInternalAsync(
            rollupRtId, from, to, rtIdScope, trigger, cancellationToken, adoptExistingJob: null);

        // AB#5189: a manually triggered recompute that died part-way records what it did not finish,
        // so the next drain picks the remainder up. Attempt zero — this is the obligation's first
        // life; the drain counts the retries from here and eventually gives up (MaxRangeAttempts).
        if (outcome.UnfinishedFrom is { } unfinishedFrom)
        {
            await _stateStore.EnqueueRecomputeRangesAsync(rollupRtId,
                new[]
                {
                    new ArchiveRecomputeRange(
                        rollupRtId, unfinishedFrom, outcome.UnfinishedTo, rtIdScope, _clock()),
                });
        }

        return outcome.Job;
    }

    /// <summary>
    /// Result of one core recompute run (AB#5189): the job as persisted, plus the part of the
    /// requested range that never committed. <see cref="UnfinishedFrom"/> is <c>null</c> when
    /// nothing is outstanding — a completed run, a coalesced one (the range was folded into the
    /// active job), or a failure the caller must not retry (the rollup is gone or not activated).
    /// </summary>
    private readonly record struct RecomputeRunOutcome(
        RecomputeJobSnapshot Job,
        DateTime? UnfinishedFrom,
        DateTime UnfinishedTo);

    /// <summary>
    /// Core recompute worker shared by the public <see cref="RecomputeArchiveAsync"/> entry point and
    /// the background <see cref="TickAsync"/> drain. When <paramref name="adoptExistingJob"/> is
    /// supplied (AB#4286) the coalesce check is skipped and that pre-created Pending job is transitioned
    /// Pending → Running → Completed/Failed <em>in place</em> — so a client that queued a durable
    /// background backfill polls a single, stable job id through to completion. When it is <c>null</c>
    /// the classic behaviour applies: an already-active job coalesces the range, otherwise a fresh
    /// Running job is created.
    /// </summary>
    private async Task<RecomputeRunOutcome> RecomputeArchiveInternalAsync(
        OctoObjectId rollupRtId,
        DateTime from,
        DateTime to,
        OctoObjectId? rtIdScope,
        RecomputeTrigger trigger,
        CancellationToken cancellationToken,
        RecomputeJobSnapshot? adoptExistingJob)
    {
        var now = _clock();

        var rollup = await _rollupStore.GetAsync(rollupRtId);
        if (rollup is null)
        {
            var failFast = await FailImmediatelyAsync(rollupRtId, from, to, rtIdScope, trigger, now,
                "Archive is not a rollup (or has been deleted).", adoptExistingJob);
            // AB#5189: no outstanding work — the target does not exist, so there is nothing to retry.
            return new RecomputeRunOutcome(failFast, null, to);
        }

        // Fail fast on a non-activated rollup. The background drain (TickAsync) already skips
        // non-activated rollups so a transiently-disabled rollup keeps its queued work; this guard is
        // for the manual entry point, where silently accepting the request and returning a job that the
        // drain will never pick up strands the caller on a job stuck at Pending with no feedback. Only
        // reachable when adoptExistingJob is null (the drain pre-filters Activated before adopting).
        if (rollup.Status != CkArchiveStatus.Activated)
        {
            var failFast = await FailImmediatelyAsync(rollupRtId, from, to, rtIdScope, trigger, now,
                $"Archive is not activated (status {rollup.Status}) — activate it before recomputing.",
                adoptExistingJob);
            // AB#5189: no outstanding work — only the manual path reaches this; parking work on a disabled rollup would fire it on activation.
            return new RecomputeRunOutcome(failFast, null, to);
        }

        // Floor the requested [from, to) onto this rollup's own bucket grid before anything downstream
        // consumes it. The executor's bucket enumerator assumes a bucket-aligned range — it steps from
        // `from` one bucket at a time — so an un-aligned `from` (e.g. an operator picking "now", with a
        // wall-clock time-of-day, in the Studio recompute dialog) would anchor the entire regenerated
        // series off-grid, writing buckets at HH:MM:SS instead of on the bucket boundary. The automatic
        // dirty-window and backfill paths already snap via AlignRangeToBuckets; the manual entry point
        // must do the same so the coalesce-enqueue and PlanChunks below both see aligned bounds.
        var zone = BucketBoundary.ResolveZone(rollup.ReferenceTimeZone);
        (from, to) = RecomputePlanner.AlignRangeToBuckets(from, to, rollup.BucketAlignment, rollup.BucketSize, zone);

        // AB#4306: never recompute a bucket the forward pass still owns — it re-aggregates its open
        // bucket every tick at generation 0 (RollupOrchestrator). If a recompute claimed it,
        // BuildUpsertPointer would flip the per-window generation pointer for the range to a higher
        // generation, and the read path (which picks the highest generation) would mask the refresh's
        // generation-0 write — so a "this month / this year so far" total would freeze at the
        // recompute instead of tracking new source data.
        //
        // AB#5189: the frontier is `now - WatermarkLag`, NOT `now`. The forward loop closes a bucket
        // only once `bucketEnd <= now - WatermarkLag` and keeps refreshing the one before that, so
        // capping at the bucket containing `now` left every bucket inside the lag window owned by
        // both. The recompute won the read path permanently, and the late source rows the lag exists
        // to absorb — which arrive after the recompute has read the source and before the bucket
        // lag-closes — never reached the series. Mirroring the forward condition exactly makes the
        // ownership rule self-evident: buckets that are lag-closed belong to the recompute,
        // everything after belongs to the forward pass.
        var forwardFrontier = now - rollup.WatermarkLag;
        var openBucketStart = BucketBoundary.AlignDown(
            forwardFrontier, rollup.BucketAlignment, rollup.BucketSize, zone);
        if (to > openBucketStart)
        {
            to = openBucketStart;
        }
        if (to <= from)
        {
            _logger.LogDebug(
                "Recompute of {RollupRtId}: requested range holds no lag-closed bucket (from {From:O}, " +
                "forward frontier {Frontier:O} = now - watermark lag {Lag}) — nothing to recompute; " +
                "the forward pass still owns those buckets.",
                rollupRtId, from, forwardFrontier, rollup.WatermarkLag);
            // No outstanding work: those buckets are the forward pass's, and it rewrites them from
            // the source on every tick until they lag-close — so a correction inside the lag window
            // lands through that path, not through a recompute obligation parked here.
            var nothingClosed = await PersistNewJobAsync(new RecomputeJobSnapshot(
                OctoObjectId.Empty, rollupRtId, RecomputeJobState.Completed, trigger,
                from, from, rtIdScope, null, null, now, now, 0, null, null));
            return new RecomputeRunOutcome(nothingClosed, null, to);
        }

        if (adoptExistingJob is null)
        {
            var active = await _jobStore.GetActiveForArchiveAsync(rollupRtId);
            if (active is not null)
            {
                await _stateStore.EnqueueRecomputeRangesAsync(rollupRtId,
                    new[] { new ArchiveRecomputeRange(rollupRtId, from, to, rtIdScope, now) });

                _logger.LogInformation(
                    "Recompute of {RollupRtId} range [{From:O},{To:O}) coalesced into active job {ActiveJob}",
                    rollupRtId, from, to, active.RtId);

                // No outstanding work for the caller: the range was just enqueued above, so the
                // active job's own drain owns it now.
                var coalesced = await PersistNewJobAsync(new RecomputeJobSnapshot(
                    OctoObjectId.Empty, rollupRtId, RecomputeJobState.Coalesced, trigger,
                    from, to, rtIdScope, null, null, now, now, 0, null, null));
                return new RecomputeRunOutcome(coalesced, null, to);
            }
        }

        // AB#5157: split [from, to) into one segment per source along the validity spans BEFORE
        // chunking, so every executor call is served by exactly one source. Buckets no span covers
        // are simply not recomputed (the forward pass writes no row for them either). Every
        // segment's source must resolve up front — a missing one fails the job before it starts,
        // exactly as the single-source path did.
        var segments = PlanSourceSegments(rollup, from, to);
        var sourcesByRtId = new Dictionary<OctoObjectId, ArchiveSnapshot>();
        foreach (var segment in segments)
        {
            if (sourcesByRtId.ContainsKey(segment.Reference.SourceArchiveRtId))
            {
                continue;
            }

            var source = await _archiveStore.GetAsync(segment.Reference.SourceArchiveRtId);
            if (source is null)
            {
                // AB#5189: nothing ran, so the WHOLE range is still outstanding. A missing source can
                // be transient (a chained rollup that is not activated yet); the caller's attempt cap
                // is what stops a permanently misconfigured rollup from retrying forever.
                var sourceMissing = await FailImmediatelyAsync(rollupRtId, from, to, rtIdScope, trigger, now,
                    $"Source archive {segment.Reference.SourceArchiveRtId} not found.", adoptExistingJob);
                return new RecomputeRunOutcome(sourceMissing, from, to);
            }

            sourcesByRtId[segment.Reference.SourceArchiveRtId] = source;
        }

        if (segments.Count == 0)
        {
            _logger.LogInformation(
                "Recompute of rollup {RollupRtId} range [{From:O},{To:O}) lies in no source's validity span — nothing to recompute.",
                rollupRtId, from, to);
        }

        var startedAt = now;

        // AB#5189: start of the part of [from, to) that has not been committed yet. It advances to
        // each chunk's start as the loop walks the range, so the catch below knows exactly what is
        // still outstanding. Before the first chunk runs, that is the whole range.
        var unfinishedFrom = from;

        RecomputeJobSnapshot job;
        if (adoptExistingJob is not null)
        {
            // Transition the pre-created Pending job to Running in place — its RtId is what the client
            // is polling, so a fresh CreateAsync would strand the caller on a job that never advances.
            job = adoptExistingJob with
            {
                State = RecomputeJobState.Running,
                Trigger = trigger,
                RangeStart = from,
                RangeEnd = to,
                RtIdScope = rtIdScope,
                StartedAt = startedAt,
                FinishedAt = null,
                ErrorReason = null,
            };
            await _jobStore.UpdateAsync(job);
        }
        else
        {
            job = await PersistNewJobAsync(new RecomputeJobSnapshot(
                OctoObjectId.Empty, rollupRtId, RecomputeJobState.Running, trigger,
                from, to, rtIdScope, null, null, startedAt, null, null, null, null));
        }

        await _stateStore.MarkRecomputeStartedAsync(rollupRtId, startedAt);

        try
        {
            // AB#4283: split the (possibly decade-long) range into bucket-aligned chunks so every
            // executor sub-run keeps its CrateDB statements under the per-statement timeout. Each chunk
            // swap is atomic on its own sub-range; totals accumulate into this one job. Chunks are
            // planned per source segment (AB#5157) and run sequentially in time order, each with its
            // segment's source snapshot; a segment boundary is always a bucket boundary, so no chunk
            // straddles two sources.
            var chunks = new List<(ArchiveSnapshot Source, DateTime Start, DateTime End)>();
            foreach (var segment in segments)
            {
                var segmentSource = sourcesByRtId[segment.Reference.SourceArchiveRtId];
                foreach (var (chunkStart, chunkEnd) in RecomputePlanner.PlanChunks(
                             segment.From, segment.To, rollup.BucketAlignment, rollup.BucketSize, _maxBucketsPerChunk,
                             BucketBoundary.ResolveZone(rollup.ReferenceTimeZone)))
                {
                    chunks.Add((segmentSource, chunkStart, chunkEnd));
                }

                if (rollup.Sources.Count > 1)
                {
                    _logger.LogInformation(
                        "Recompute of rollup {RollupRtId}: segment [{SegmentStart:O},{SegmentEnd:O}) is served by source {SourceRtId}.",
                        rollupRtId, segment.From, segment.To, segment.Reference.SourceArchiveRtId);
                }
            }

            if (chunks.Count > 1)
            {
                _logger.LogInformation(
                    "Recompute of rollup {RollupRtId} range [{From:O},{To:O}) split into {ChunkCount} chunks of ≤{MaxBuckets} buckets.",
                    rollupRtId, from, to, chunks.Count, _maxBucketsPerChunk);
            }

            var totalRows = 0;
            var totalWindows = 0;
            var chunkIndex = 0;
            foreach (var (source, chunkStart, chunkEnd) in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // AB#5189: remember where the still-unfinished tail begins, so a failure below can
                // hand [unfinishedFrom, to) back as outstanding work instead of dropping it.
                unfinishedFrom = chunkStart;

                var chunkResult = await ExecuteChunkWithRetryAsync(
                    source, rollup, chunkStart, chunkEnd, rtIdScope, chunkIndex + 1, chunks.Count, cancellationToken);
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

            // AB#5157: a recompute (above all a backfill) can extend the rollup's stored range far
            // beyond what the TTL would surface in time; drop the memoised coverage so the next
            // coverage query re-measures.
            _coverageInvalidator?.Invalidate(_tenantId, rollupRtId);

            // Chain: this rollup's values changed in [from, to) → its direct dependents are stale.
            // The full range is propagated; each dependent clips it to its own reference span.
            await EnqueueOnDirectDependentsAsync(rollupRtId, from, to, cancellationToken);

            return new RecomputeRunOutcome(completed, null, to);
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
                "Recompute of rollup {RollupRtId} range [{From:O},{To:O}) failed; " +
                "[{UnfinishedFrom:O},{To:O}) is still outstanding",
                rollupRtId, from, to, unfinishedFrom, to);

            // AB#5189: the chunks before the failure stay committed, but the rest of the range must
            // not evaporate. The caller records it as outstanding — the manual entry point at attempt
            // zero, the drain with its attempt bookkeeping — so exactly one place decides how a
            // remainder is retried. Chunking (AB#4283) is what turned AB#4184's per-job "a failed run
            // leaves previous values intact" into a per-chunk guarantee; handing the remainder back
            // restores the per-job one.
            return new RecomputeRunOutcome(failed, unfinishedFrom < to ? unfinishedFrom : null, to);
        }
    }

    /// <summary>
    /// Durable, background backfill (AB#4269 / AB#4286): queue a whole-history recompute of a rollup
    /// without running it inline. Resolves the earliest stored timestamp over all source archives —
    /// each raised to its <c>ValidFrom</c> (AB#5157) — snaps it down to the rollup's bucket boundary,
    /// pre-creates a <see cref="RecomputeJobState.Pending"/> job and enqueues a persisted pending
    /// recompute range <c>[sourceMin, now)</c> — then returns the Pending job immediately. The heavy
    /// recompute runs later on the background <see cref="TickAsync"/> (which adopts the Pending job and
    /// drives it to Completed under the host application-lifetime token), so it is never bound to —
    /// and can never be cancelled by — the client HTTP request.
    /// <para>
    /// A source without data, or whose data lies entirely outside its validity span, contributes
    /// nothing; when no source contributes the backfill is a no-op (returns <c>null</c>). A non-rollup
    /// target produces a failed job, exactly as <see cref="RecomputeArchiveAsync"/> would. When a
    /// recompute job is already active for the rollup, the range is folded into it and that active job
    /// is returned so the caller polls a single job id.
    /// </para>
    /// </summary>
    public async Task<RecomputeJobSnapshot?> EnqueueBackfillFromSourceAsync(
        OctoObjectId rollupRtId, CancellationToken cancellationToken)
    {
        var rollup = await _rollupStore.GetAsync(rollupRtId);
        if (rollup is null)
        {
            // Not a rollup (or deleted): return the same failed-job shape recomputeArchive would for a
            // non-rollup target, without resolving source-min.
            var nowForFailure = _clock();
            return await FailImmediatelyAsync(
                rollupRtId, nowForFailure, nowForFailure, rtIdScope: null, RecomputeTrigger.Manual,
                nowForFailure, "Archive is not a rollup (or has been deleted).");
        }

        // Fail fast on a non-activated rollup instead of pre-creating a Pending job + range that the
        // background drain (TickAsync) will silently skip forever — the caller would otherwise poll a
        // job stuck at Pending with no error. Mirror the non-rollup fail shape above.
        if (rollup.Status != CkArchiveStatus.Activated)
        {
            var nowForFailure = _clock();
            return await FailImmediatelyAsync(
                rollupRtId, nowForFailure, nowForFailure, rtIdScope: null, RecomputeTrigger.Manual,
                nowForFailure,
                $"Archive is not activated (status {rollup.Status}) — activate it before backfilling.");
        }

        // AB#5157: backfill start = MIN over sources of max(earliest stored timestamp, ValidFrom).
        // A source without data contributes nothing; nor does one whose data lies wholly outside its
        // validity span — the rollup would never read it. AB#5189: coverage (MIN..MAX) is read
        // instead of the bare minimum so BOTH ends of that test can be made: a source whose data
        // starts at or after its ValidTo used to be skipped, but one whose data ENDS before its
        // ValidFrom still raised the candidate to its own ValidFrom and opened a backfill over
        // buckets it cannot contribute a single row to.
        DateTime? sourceMin = null;
        foreach (var reference in rollup.Sources)
        {
            var coverage = await _streamData.GetArchiveCoverageAsync(reference.SourceArchiveRtId, cancellationToken);
            if (coverage is null)
            {
                _logger.LogDebug(
                    "Backfill of rollup {RollupRtId}: source archive {SourceRtId} holds no data — contributes nothing.",
                    rollupRtId, reference.SourceArchiveRtId);
                continue;
            }

            var archiveMin = coverage.AvailableFrom;

            var candidate = reference.ValidFrom is { } validFrom && validFrom > archiveMin
                ? validFrom
                : archiveMin;

            if (reference.ValidTo is { } validTo && candidate >= validTo)
            {
                _logger.LogDebug(
                    "Backfill of rollup {RollupRtId}: source archive {SourceRtId} holds data only from {ArchiveMin:O}, at or after its ValidTo {ValidTo:O} — contributes nothing.",
                    rollupRtId, reference.SourceArchiveRtId, archiveMin, validTo);
                continue;
            }

            // The mirror image (AB#5189): all of this source's data lies before its span starts.
            // Compared strictly: AvailableTo is an exclusive window end for windowed archives but the
            // last (inclusive) timestamp for raw ones, and a raw point sitting exactly on ValidFrom is
            // inside the half-open span. Being strict keeps the skip conservative — it can only ever
            // drop a source that truly cannot serve a bucket.
            if (reference.ValidFrom is { } spanStart && coverage.AvailableTo < spanStart)
            {
                _logger.LogDebug(
                    "Backfill of rollup {RollupRtId}: source archive {SourceRtId} holds data only up to {ArchiveMax:O}, before its ValidFrom {ValidFrom:O} — contributes nothing.",
                    rollupRtId, reference.SourceArchiveRtId, coverage.AvailableTo, spanStart);
                continue;
            }

            if (sourceMin is null || candidate < sourceMin.Value)
            {
                sourceMin = candidate;
            }
        }

        if (sourceMin is null)
        {
            _logger.LogInformation(
                "Backfill of rollup {RollupRtId}: no source archive holds data inside its validity span — nothing to queue (no-op).",
                rollupRtId);
            return null;
        }

        var now = _clock();

        // Snap the earliest in-span timestamp down to the rollup's bucket boundary so the recompute
        // starts on a clean bucket-start; recompute the whole history [from, now).
        var (from, _) = RecomputePlanner.AlignRangeToBuckets(
            sourceMin.Value, sourceMin.Value, rollup.BucketAlignment, rollup.BucketSize,
            BucketBoundary.ResolveZone(rollup.ReferenceTimeZone));

        if (now <= from)
        {
            _logger.LogInformation(
                "Backfill of rollup {RollupRtId}: aligned source start {From:O} is not before now {Now:O} — nothing to queue (no-op).",
                rollupRtId, from, now);
            return null;
        }

        // Coalesce: if a recompute job is already queued/running for this rollup, fold the whole-history
        // range into its pending work list and hand the caller that job so they poll a single id.
        var active = await _jobStore.GetActiveForArchiveAsync(rollupRtId);
        if (active is not null)
        {
            await _stateStore.EnqueueRecomputeRangesAsync(rollupRtId,
                new[] { new ArchiveRecomputeRange(rollupRtId, from, now, null, now) });
            _logger.LogInformation(
                "Backfill of rollup {RollupRtId} over [{From:O}, {Now:O}) folded into already-active recompute job {JobId}.",
                rollupRtId, from, now, active.RtId);
            return active;
        }

        // Pre-create the Pending job first (persisted → survives an asset-repo restart), then enqueue
        // the persisted pending range. Ordering matters: if a tick fires between the two writes it sees
        // the Pending job but no range yet and simply skips — no orphaned throwaway job is created.
        var pending = await PersistNewJobAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, rollupRtId, RecomputeJobState.Pending, RecomputeTrigger.Manual,
            from, now, null, null, null, null, null, null, null, null));

        await _stateStore.EnqueueRecomputeRangesAsync(rollupRtId,
            new[] { new ArchiveRecomputeRange(rollupRtId, from, now, null, now) });

        _logger.LogInformation(
            "Backfill of rollup {RollupRtId} queued as job {JobId} over [{From:O}, {Now:O}) from {SourceCount} source(s) " +
            "(earliest in-span source ts {SourceMin:O}); background recompute orchestrator will run it.",
            rollupRtId, pending.RtId, from, now, rollup.Sources.Count, sourceMin.Value);

        return pending;
    }

    /// <summary>
    /// Highest attempt count among the pending obligations that overlap <c>[start, end)</c>
    /// (AB#5189). <see cref="RecomputePlanner.MergeIntervals"/> collapses several obligations into
    /// one interval and drops everything but the bounds, so the attempt count has to be recovered
    /// from the ranges that fed it. Taking the maximum means merging a fresh obligation into a range
    /// that is already close to the cap cannot reset the counter and restart the retry loop.
    /// </summary>
    private static int MaxAttemptsCovering(
        IReadOnlyList<ArchiveRecomputeRange> ranges, DateTime start, DateTime end)
    {
        var attempts = 0;
        foreach (var range in ranges)
        {
            if (range.RangeStart < end && start < range.RangeEnd && range.Attempts > attempts)
            {
                attempts = range.Attempts;
            }
        }

        return attempts;
    }

    /// <summary>
    /// Exponential per-range backoff (AB#5189): the <c>n</c>-th failure waits
    /// <c>base * 2^(n-1)</c>, capped at <see cref="MaxRangeRetryDelay"/> so a large attempt count
    /// can neither overflow nor park an obligation for days.
    /// </summary>
    private TimeSpan RangeRetryDelay(int attempts)
    {
        var shift = Math.Min(attempts - 1, 16);
        var ticks = _rangeRetryBaseDelay.Ticks * (1L << shift);
        return ticks >= MaxRangeRetryDelay.Ticks ? MaxRangeRetryDelay : TimeSpan.FromTicks(ticks);
    }

    /// <summary>
    /// Splits the bucket-aligned range <c>[from, to)</c> into one segment per source whose validity
    /// span intersects it (AB#5157), ordered by start. Spans are pairwise disjoint and lie on the
    /// rollup's bucket grid, so the segments are disjoint and bucket-aligned too; a sub-range no span
    /// covers yields no segment.
    /// </summary>
    private static List<(RollupSourceReference Reference, DateTime From, DateTime To)> PlanSourceSegments(
        RollupArchiveSnapshot rollup, DateTime from, DateTime to)
    {
        var segments = new List<(RollupSourceReference Reference, DateTime From, DateTime To)>(rollup.Sources.Count);
        foreach (var reference in rollup.Sources)
        {
            if (reference.Clip(from, to) is { } clipped)
            {
                segments.Add((reference, clipped.From, clipped.To));
            }
        }

        segments.Sort(static (a, b) => a.From.CompareTo(b.From));
        return segments;
    }

    /// <summary>
    /// Runs one chunk's executor call under a bounded retry (AB#4278). Each chunk is idempotent — its
    /// staging→live swap is keyed by the per-window generation pointer — so replaying the whole chunk
    /// after a transient CrateDB connection drop re-commits the same rows without duplication. Only a
    /// transient connection-class failure (a dropped connector, <c>EndOfStreamException</c>,
    /// socket/IO error, or a CrateDB health blip) is retried; a deterministic error (bad SQL, missing
    /// table, cancellation) is rethrown immediately so the job fails fast with the real cause. When the
    /// attempt budget is exhausted the last exception propagates and fails the job.
    /// </summary>
    private async Task<RecomputeExecutionResult> ExecuteChunkWithRetryAsync(
        ArchiveSnapshot source,
        RollupArchiveSnapshot rollup,
        DateTime chunkStart,
        DateTime chunkEnd,
        OctoObjectId? rtIdScope,
        int chunkNumber,
        int chunkCount,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _executor.ExecuteAsync(
                    source, rollup, chunkStart, chunkEnd, rtIdScope, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Deliberate shutdown / caller cancellation — never retry.
                throw;
            }
            catch (Exception ex) when (attempt < _maxChunkAttempts && IsTransientConnectionFailure(ex))
            {
                var backoff = TimeSpan.FromTicks(_chunkRetryBaseDelay.Ticks * (1L << (attempt - 1)));
                _logger.LogWarning(ex,
                    "Recompute of rollup {RollupRtId}: chunk {ChunkNumber}/{ChunkCount} [{ChunkStart:O},{ChunkEnd:O}) " +
                    "attempt {Attempt}/{MaxAttempts} hit a transient CrateDB connection failure — retrying in {BackoffMs}ms.",
                    rollup.RtId, chunkNumber, chunkCount, chunkStart, chunkEnd,
                    attempt, _maxChunkAttempts, (long)backoff.TotalMilliseconds);

                await _delay(backoff, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Classifies an exception as a transient CrateDB connection failure worth retrying the whole
    /// chunk for (AB#4278). Walks the inner-exception chain. A server-side rejection
    /// (<c>PostgresException</c>) is explicitly NOT transient — the SQL is wrong and a retry can't fix
    /// it. Otherwise the dropped-connection class is matched by type (Npgsql connector exceptions,
    /// <see cref="System.IO.IOException"/> incl. <see cref="System.IO.EndOfStreamException"/>, socket
    /// errors) and by message signature (the exact strings CrateDB/Npgsql emit on a mid-read drop and
    /// on a health blip). Matched by name/message because this engine layer does not reference Npgsql.
    /// </summary>
    private static bool IsTransientConnectionFailure(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            var typeName = ex.GetType().FullName ?? string.Empty;

            // Server rejected the statement — deterministic, retrying won't help.
            if (typeName.Contains("PostgresException", StringComparison.Ordinal))
            {
                return false;
            }

            // IOException covers EndOfStreamException ("Attempted to read past the end of the stream").
            if (ex is System.IO.IOException)
            {
                return true;
            }

            if (typeName.Contains("NpgsqlException", StringComparison.Ordinal)
                || typeName.Contains("SocketException", StringComparison.Ordinal))
            {
                return true;
            }

            var message = ex.Message ?? string.Empty;
            if (message.Contains("reading from stream", StringComparison.OrdinalIgnoreCase)
                || message.Contains("read past the end of the stream", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                || message.Contains("broken", StringComparison.OrdinalIgnoreCase)
                || message.Contains("is unhealthy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// <para>
    /// <paramref name="maxRetroReach"/> is the AB#4196 bounded-retro-reach cap. When non-null, each
    /// dependent's stale range start is floored at <c>dependentWatermark - maxRetroReach</c>, so a
    /// single very-late change can never drag an automatic recompute further back than the cap. Null
    /// (the default, used by chain propagation after a committed recompute) leaves the range unbounded
    /// — a manual / chained recompute is deliberately not capped.
    /// </para>
    /// <para>
    /// AB#5157: a dependent is a direct child when <paramref name="sourceRtId"/> is among its
    /// sources, and the dirty range is clipped to that reference's validity span on both ends before
    /// it is bucket-aligned — a change in the writing source outside the window it is authoritative
    /// for cannot make the dependent stale. An empty clip enqueues nothing.
    /// </para>
    /// <para>
    /// A degenerate window (<paramref name="to"/> &lt;= <paramref name="from"/>) is widened to
    /// <c>[from, from + 1 tick)</c> before clipping. The retroactive detector persists a
    /// single-timestamp correction as <c>[t, t + 1 tick)</c>, which Mongo's millisecond resolution
    /// collapses to <c>[t, t)</c>; before AB#5157 the bucket alignment alone turned that into one
    /// bucket, whereas the half-open span clip would discard it. Widening keeps the point exactly
    /// when it lies inside the span — a point at <c>ValidTo</c> stays outside.
    /// </para>
    /// </remarks>
    private async Task EnqueueOnDirectDependentsAsync(
        OctoObjectId sourceRtId, DateTime from, DateTime to, CancellationToken cancellationToken,
        TimeSpan? maxRetroReach = null)
    {
        if (to <= from)
        {
            to = from.AddTicks(1);
        }

        var dependents = await _dependencyGraph.GetTransitiveDependentsAsync(sourceRtId);
        foreach (var dependent in dependents)
        {
            // Direct children only; deeper levels are reached when each child recomputes and
            // propagates to its own dependents. Re-deriving the range per dependent honours that
            // dependent's own bucket alignment.
            if (!dependent.HasSource(sourceRtId))
            {
                continue;
            }

            var reference = dependent.Sources.First(s => s.SourceArchiveRtId == sourceRtId);
            if (reference.Clip(from, to) is not { } clipped)
            {
                continue;
            }

            var (start, end) = RecomputePlanner.AlignRangeToBuckets(
                clipped.From, clipped.To, dependent.BucketAlignment, dependent.BucketSize,
                BucketBoundary.ResolveZone(dependent.ReferenceTimeZone));

            // AB#4336 decision D2: a retroactive source change inside a bucket also changes the
            // LOCF carry-in of the NEXT bucket when this dependent materialises a TimeWeightedAvg —
            // the opening state is derived from the last source row before the bucket. Extend the
            // stale range by one bucket; a correction followed by a longer silent stretch is the
            // operator's manual recomputeArchive escape hatch (concept-time-weighted §5.4). The
            // AB#4288 clamp below still applies — a not-yet-aggregated successor bucket gets its
            // fresh carry from normal forward aggregation anyway.
            if (dependent.Aggregations.Any(a => a.Function == CkRollupFunction.TimeWeightedAvg))
            {
                end = BucketBoundary.NextBucketEnd(
                    end, dependent.BucketAlignment, dependent.BucketSize,
                    BucketBoundary.ResolveZone(dependent.ReferenceTimeZone));

                // AB#5157: the successor bucket past the span end is served by ANOTHER source,
                // whose carry-in comes from that source's own rows — a change in this source cannot
                // affect it. Keep the extension inside the span.
                if (reference.ValidTo is { } validTo && end > validTo)
                {
                    end = validTo;
                }
            }

            // Clamp to what this dependent has actually aggregated (AB#4288). The retroactive-write
            // detector flags a write when it is retroactive for the MOST-advanced dependent (max
            // watermark), so a source can hand this dependent a window it has not reached yet. A
            // dependent whose own LastAggregatedBucketEnd is at or before the window start has not
            // aggregated it — recomputing would write a partial, not-yet-closed bucket; skip it and
            // let its normal forward aggregation consume the corrected source data. Otherwise keep
            // only the already-aggregated prefix [start, min(end, LastAggregatedBucketEnd)). Both
            // bounds are bucket boundaries for this dependent, so the clamp stays bucket-aligned.
            if (dependent.LastAggregatedBucketEnd is not { } aggregatedEnd || start >= aggregatedEnd)
            {
                continue;
            }

            var clampedEnd = end < aggregatedEnd ? end : aggregatedEnd;

            // AB#4196: floor the stale-range start at (this dependent's watermark - cap) so a single
            // very-late change can never drag an automatic recompute further back than the cap. The
            // floor is snapped down to this dependent's bucket grid to keep both bounds bucket-aligned.
            // Null cap (chain propagation) leaves start untouched.
            if (maxRetroReach is { } reach)
            {
                var reachFloor = BucketBoundary.AlignDown(
                    aggregatedEnd - reach, dependent.BucketAlignment, dependent.BucketSize,
                    BucketBoundary.ResolveZone(dependent.ReferenceTimeZone));
                if (reachFloor > start)
                {
                    start = reachFloor;
                }
            }

            // Nothing in reach: either the AB#4288 clamp or the AB#4196 floor pushed start to/past the
            // effective end (e.g. the whole window predates the cap). Skip rather than enqueue an
            // inverted range.
            if (start >= clampedEnd)
            {
                continue;
            }

            await _stateStore.EnqueueRecomputeRangesAsync(dependent.RtId,
                new[] { new ArchiveRecomputeRange(dependent.RtId, start, clampedEnd, null, _clock()) });
        }
    }

    private async Task<RecomputeJobSnapshot> PersistNewJobAsync(RecomputeJobSnapshot job)
    {
        var rtId = await _jobStore.CreateAsync(job);
        return job with { RtId = rtId };
    }

    private async Task<RecomputeJobSnapshot> FailImmediatelyAsync(
        OctoObjectId rollupRtId, DateTime from, DateTime to, OctoObjectId? rtIdScope,
        RecomputeTrigger trigger, DateTime now, string reason,
        RecomputeJobSnapshot? adoptExistingJob = null)
    {
        RecomputeJobSnapshot failed;
        if (adoptExistingJob is not null)
        {
            // Fail the pre-created Pending job in place so the polling client sees the terminal state.
            failed = adoptExistingJob with
            {
                State = RecomputeJobState.Failed,
                Trigger = trigger,
                RangeStart = from,
                RangeEnd = to,
                RtIdScope = rtIdScope,
                FinishedAt = now,
                DurationMs = 0,
                ErrorReason = reason,
            };
            await _jobStore.UpdateAsync(failed);
        }
        else
        {
            failed = await PersistNewJobAsync(new RecomputeJobSnapshot(
                OctoObjectId.Empty, rollupRtId, RecomputeJobState.Failed, trigger,
                from, to, rtIdScope, null, null, now, now, 0, reason, null));
        }

        await _audit.RecordRecomputeFailureAsync(_tenantId, rollupRtId, from, to, reason);
        _logger.LogWarning("Recompute of {RollupRtId} not started: {Reason}", rollupRtId, reason);
        return failed;
    }
}
