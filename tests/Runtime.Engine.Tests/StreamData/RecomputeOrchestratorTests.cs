using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RecomputeOrchestratorTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId RollupRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId JobRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly DateTime From = new(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IRollupDependencyGraph _graph = A.Fake<IRollupDependencyGraph>();
    private readonly IArchiveRecomputeStateStore _stateStore = A.Fake<IArchiveRecomputeStateStore>();
    private readonly IRecomputeJobStore _jobStore = A.Fake<IRecomputeJobStore>();
    private readonly IArchiveRecomputeExecutor _executor = A.Fake<IArchiveRecomputeExecutor>();
    private readonly IStreamDataRepository _streamData = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    public RecomputeOrchestratorTests()
    {
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).Returns(JobRt);
        A.CallTo(() => _jobStore.GetActiveForArchiveAsync(A<OctoObjectId>._)).Returns((RecomputeJobSnapshot?)null);
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(A<OctoObjectId>._))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)Array.Empty<ArchiveDirtyWindow>());
        A.CallTo(() => _stateStore.GetPendingRecomputeRangesAsync(A<OctoObjectId>._))
            .Returns((IReadOnlyList<ArchiveRecomputeRange>)Array.Empty<ArchiveRecomputeRange>());
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(A<OctoObjectId>._))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)Array.Empty<RollupArchiveSnapshot>());
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .Returns(new RecomputeExecutionResult(42, 3));
    }

    private RecomputeOrchestrator NewSut(int maxBucketsPerChunk = RecomputeOrchestrator.DefaultMaxBucketsPerChunk) =>
        new(TenantId, _archiveStore, _rollupStore, _graph, _stateStore, _jobStore, _executor, _streamData, _audit,
            NullLogger<RecomputeOrchestrator>.Instance, () => Now, maxBucketsPerChunk);

    // Retry-configured SUT (AB#4278): a no-op delay keeps the test synchronous/fast so the backoff
    // schedule doesn't add real wall-clock time.
    private RecomputeOrchestrator NewRetrySut(int maxChunkAttempts) =>
        new(TenantId, _archiveStore, _rollupStore, _graph, _stateStore, _jobStore, _executor, _streamData, _audit,
            NullLogger<RecomputeOrchestrator>.Instance, () => Now,
            RecomputeOrchestrator.DefaultMaxBucketsPerChunk,
            maxChunkAttempts,
            chunkRetryBaseDelay: TimeSpan.Zero,
            delay: (_, _) => Task.CompletedTask);


    // AB#5189: the backfill reads the source's coverage (MIN..MAX) rather than the bare minimum, so
    // it can also skip a source whose data ENDS before its validity span starts. These tests pin the
    // start; an open upper bound keeps every source in play, matching the pre-AB#5189 behaviour.
    private static ArchiveCoverage? Coverage(DateTime? min, DateTime? max = null) =>
        min is null ? null : new ArchiveCoverage(min.Value, max ?? DateTime.MaxValue);

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId rtId, OctoObjectId sourceRtId, CkArchiveStatus status = CkArchiveStatus.Activated,
        DateTime? lastAggregatedBucketEnd = null) =>
        Rollup(rtId, new[] { new RollupSourceReference(sourceRtId) }, status, lastAggregatedBucketEnd);

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId rtId, IReadOnlyList<RollupSourceReference> sources,
        CkArchiveStatus status = CkArchiveStatus.Activated,
        DateTime? lastAggregatedBucketEnd = null) =>
        new(rtId, TargetType, status, null, sources,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), lastAggregatedBucketEnd,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null);

    private static ArchiveSnapshot Source() =>
        new(SourceRt, new RtCkId<CkTypeId>("Test", new CkTypeId("TempSensor")),
            CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    // AB#4196: a source archive carrying a bounded-retro-reach cap.
    private static ArchiveSnapshot SourceWithCap(long capMs) => Source() with { MaxRetroactiveReachMs = capMs };

    private void StubRollupAndSource() =>
        StubRollupAndSource(Rollup(RollupRt, SourceRt));

    private void StubRollupAndSource(RollupArchiveSnapshot rollup)
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(rollup);
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Source());
    }

    // ---- RecomputeArchiveAsync: happy path ---------------------------------------------------

    [Fact]
    public async Task Recompute_HappyPath_RunsExecutorAndCompletes()
    {
        StubRollupAndSource();

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.Equal(42, job.RowsProcessed);
        Assert.Equal(3, job.WindowsProcessed);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, From, To, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.MarkRecomputeStartedAsync(RollupRt, Now)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.MarkRecomputeSucceededAsync(RollupRt, Now)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordRecomputeRunAsync(TenantId, RollupRt, From, To, 42, 3, A<TimeSpan>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _jobStore.UpdateAsync(A<RecomputeJobSnapshot>.That.Matches(j => j.State == RecomputeJobState.Completed)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_UnalignedRange_FloorsToBucketBoundaryBeforeExecuting()
    {
        // Regression: the Studio recompute dialog pre-fills `from`/`to` with a wall-clock "now"
        // (time-of-day incl. sub-second). The manual entry point must snap the range onto the rollup's
        // bucket grid — exactly like the automatic and backfill paths — before the executor's bucket
        // enumerator (which steps from `from`) consumes it. Otherwise every regenerated bucket lands at
        // 10:00:48.554 instead of 10:00:00, poisoning the whole series. BucketSize here is 1h.
        StubRollupAndSource();
        var unalignedFrom = new DateTime(2026, 5, 11, 10, 0, 48, 554, DateTimeKind.Utc);

        await NewSut().RecomputeArchiveAsync(
            RollupRt, unalignedFrom, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        // Executor sees the floored From (10:00:00), never the raw 10:00:48.554.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, From, To, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, unalignedFrom, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_Success_PropagatesToDirectDependentsOnly()
    {
        StubRollupAndSource();
        // Watermark after the window so the AB#4288 per-dependent clamp keeps the full range.
        var directChild = Rollup(OctoObjectId.GenerateNewId(), RollupRt, lastAggregatedBucketEnd: Now);
        var grandChild = Rollup(OctoObjectId.GenerateNewId(), directChild.RtId, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(RollupRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { directChild, grandChild });

        await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(directChild.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(grandChild.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    // ---- Chunking (AB#4283) ------------------------------------------------------------------

    [Fact]
    public async Task Recompute_LargeRange_SplitsIntoChunks_AndAccumulatesTotals()
    {
        StubRollupAndSource(); // bucket size = 1h, FixedSize alignment

        // 5 hourly buckets with a chunk cap of 2 → 3 chunks: [0,2h), [2,4h), [4,5h).
        var from = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 11, 5, 0, 0, DateTimeKind.Utc);

        var job = await NewSut(maxBucketsPerChunk: 2)
            .RecomputeArchiveAsync(RollupRt, from, to, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        // Executor returns (42, 3) per call; three chunks → totals accumulate.
        Assert.Equal(126, job.RowsProcessed);
        Assert.Equal(9, job.WindowsProcessed);

        // Exactly three executor calls, one per contiguous chunk, no bucket split or overlap.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                from, new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc), null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 4, 0, 0, DateTimeKind.Utc), null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                new DateTime(2026, 5, 11, 4, 0, 0, DateTimeKind.Utc), to, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);

        // Audit records the whole [from, to) range with the accumulated totals.
        A.CallTo(() => _audit.RecordRecomputeRunAsync(TenantId, RollupRt, from, to, 126, 9, A<TimeSpan>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_LargeRange_MidChunkFailure_LeavesPriorChunksCommitted()
    {
        StubRollupAndSource();
        var from = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 11, 5, 0, 0, DateTimeKind.Utc);

        // Second chunk starts at 02:00 → throw there; first chunk (00:00) has already committed.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc), A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("chunk 2 exploded"));

        var job = await NewSut(maxBucketsPerChunk: 2)
            .RecomputeArchiveAsync(RollupRt, from, to, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        Assert.Equal("chunk 2 exploded", job.ErrorReason);
        // The first chunk committed before the failure (partial-progress semantics).
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                from, new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc), null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // No chain propagation on failure: dependents must not be told this range is fresh when it is
        // half old. (AB#5189 narrowed this from "no enqueue at all" — the rollup's OWN work list now
        // records the unfinished tail, which is asserted by
        // Recompute_MidChunkFailure_RecordsTheUnfinishedRemainderAsOutstanding.)
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                A<OctoObjectId>.That.Matches(id => id != RollupRt), A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    // ---- Per-chunk retry (AB#4278) -----------------------------------------------------------

    [Fact]
    public async Task Recompute_ChunkHitsTransientDrop_RetriesWholeChunk_AndJobSucceeds()
    {
        StubRollupAndSource();

        // Single chunk (1h range < default cap). The executor drops the connection on the first two
        // attempts (the exact transient class the live backfill saw), then succeeds on the third.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .Throws(new System.IO.EndOfStreamException("Attempted to read past the end of the stream.")).Once()
            .Then.Throws(new System.IO.IOException("Exception while reading from stream")).Once()
            .Then.Returns(new RecomputeExecutionResult(42, 3));

        var job = await NewRetrySut(maxChunkAttempts: 4)
            .RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.Equal(42, job.RowsProcessed);
        Assert.Equal(3, job.WindowsProcessed);
        // Three executor invocations: two failed attempts + the successful retry.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => _stateStore.MarkRecomputeSucceededAsync(RollupRt, Now)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_ChunkTransientDropExceedsBudget_FailsJobWithRealError()
    {
        StubRollupAndSource();

        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .Throws(new System.IO.IOException("Exception while reading from stream"));

        var job = await NewRetrySut(maxChunkAttempts: 3)
            .RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        Assert.Equal("Exception while reading from stream", job.ErrorReason);
        // Exactly the attempt budget was consumed (1 initial + 2 retries).
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => _stateStore.MarkRecomputeFailedAsync(RollupRt, Now, "Exception while reading from stream"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_ChunkDeterministicError_FailsFastWithoutRetry()
    {
        StubRollupAndSource();

        // A server-side rejection is not transient — must not be retried even with a retry budget.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .Throws(new InvalidOperationException("column does not exist"));

        var job = await NewRetrySut(maxChunkAttempts: 4)
            .RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        Assert.Equal("column does not exist", job.ErrorReason);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                From, To, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ---- Coalesce -----------------------------------------------------------------------------

    [Fact]
    public async Task Recompute_WhenJobActive_CoalescesWithoutRunningExecutor()
    {
        StubRollupAndSource();
        var activeJob = new RecomputeJobSnapshot(
            OctoObjectId.GenerateNewId(), RollupRt, RecomputeJobState.Running, RecomputeTrigger.Periodic,
            From, To, null, null, null, Now, null, null, null, null);
        A.CallTo(() => _jobStore.GetActiveForArchiveAsync(RollupRt)).Returns(activeJob);

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Coalesced, job.State);
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(RollupRt, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ---- Failure modes ------------------------------------------------------------------------

    [Fact]
    public async Task Recompute_ExecutorThrows_MarksFailedAndDoesNotPropagate()
    {
        StubRollupAndSource();
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("crate exploded"));

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        Assert.Equal("crate exploded", job.ErrorReason);
        A.CallTo(() => _stateStore.MarkRecomputeFailedAsync(RollupRt, Now, "crate exploded")).MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.MarkRecomputeSucceededAsync(A<OctoObjectId>._, A<DateTime>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordRecomputeFailureAsync(TenantId, RollupRt, From, To, "crate exploded")).MustHaveHappenedOnceExactly();
        // No chain propagation on failure (no DEPENDENT was enqueued). Since AB#5189 the rollup's own
        // work list does get the unfinished range back — nothing ran here, so that is all of [From, To).
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                A<OctoObjectId>.That.Matches(id => id != RollupRt), A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(RollupRt,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1 && rs[0].RangeStart == From && rs[0].RangeEnd == To)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_NotARollup_FailsWithoutRunningExecutor()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns((RollupArchiveSnapshot?)null);

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_NotActivated_FailsWithoutRunningExecutor()
    {
        // A manual recompute of a non-activated rollup must fail fast (the background drain skips
        // non-activated rollups, so accepting it would strand the caller on a job stuck at Pending).
        A.CallTo(() => _rollupStore.GetAsync(RollupRt))
            .Returns(Rollup(RollupRt, SourceRt, CkArchiveStatus.Disabled));

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
        Assert.Contains("not activated", job.ErrorReason!, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_SourceMissing_Fails()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, SourceRt));
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns((ArchiveSnapshot?)null);

        var job = await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);
    }

    // ---- PropagateDirtyWindowsAsync ----------------------------------------------------------

    [Fact]
    public async Task Propagate_RetroactiveWindow_EnqueuesAlignedRangeOnDependentAndClears()
    {
        // Watermark well after the window so the AB#4288 clamp keeps the full aligned bucket.
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // AB#4196: with the source carrying a bounded-retro-reach cap, a dirty window that reaches
    // further back than (dependent watermark - cap) has its start floored to the cap, so a single
    // very-late change can never drag the automatic recompute past the cap.
    [Fact]
    public async Task Propagate_RetroReachCap_FloorsStartToWatermarkMinusCap()
    {
        // Dependent watermark = Now (14:00), cap = 2h ⇒ floor = 12:00. The aligned window is
        // [11:00, 14:00); the start is floored from 11:00 up to 12:00.
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(SourceWithCap(2 * 60 * 60 * 1000));
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 11, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 13, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc)   // floored to watermark-cap
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // AB#4196: when the whole dirty window predates (dependent watermark - cap), nothing is in reach
    // for the automatic path and the dependent is skipped — the manual recomputeArchive stays the
    // unbounded escape hatch.
    [Fact]
    public async Task Propagate_RetroReachCap_EntireWindowBeyondCap_Skipped()
    {
        // Dependent watermark = Now (14:00), cap = 1h ⇒ floor = 13:00. Aligned window is
        // [10:00, 11:00) — entirely older than the floor.
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(SourceWithCap(60 * 60 * 1000));
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // AB#4288: the retroactive-write detector flags a write when it is retroactive for the
    // MOST-advanced dependent (max watermark), so a lagging dependent (e.g. a yearly rollup whose
    // current bucket has not closed) can be handed a window it has not aggregated yet. Such a
    // dependent must be skipped — recomputing would materialise a partial, not-yet-closed bucket.
    [Fact]
    public async Task Propagate_RetroactiveWindow_DependentWatermarkBehindWindow_IsSkipped()
    {
        // Aligned window is [10:00, 11:00); this dependent has only aggregated up to 09:00.
        var laggingChild = Rollup(OctoObjectId.GenerateNewId(), SourceRt,
            lastAggregatedBucketEnd: new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc));
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { laggingChild });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Propagate_RetroactiveWindow_DependentNeverAggregated_IsSkipped()
    {
        // Null watermark: the dependent has never aggregated anything, so nothing is stale for it.
        var freshChild = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: null);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { freshChild });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Propagate_RetroactiveWindow_DependentWatermarkMidWindow_ClampsRangeToWatermark()
    {
        // Aligned window is [10:00, 14:00); the dependent has aggregated only up to 12:00, so the
        // enqueued range is clamped to [10:00, 12:00) — the already-aggregated prefix.
        var partialChild = Rollup(OctoObjectId.GenerateNewId(), SourceRt,
            lastAggregatedBucketEnd: new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc));
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { partialChild });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 13, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                partialChild.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Propagate_AppendWindow_IsIgnored()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(From, To, RecomputeChangeKind.Append, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // ---- EnqueueBackfillFromSourceAsync (AB#4269 / AB#4286: durable background) ----------------

    [Fact]
    public async Task Backfill_ResolvesSourceMin_AlignsDownToBucket_AndEnqueuesPendingJob()
    {
        StubRollupAndSource(); // bucket size = 1h, FixedSize alignment
        var sourceMin = new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc);
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._))
            .Returns(Coverage(sourceMin));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        // Returns immediately with a Pending job — the recompute does NOT run inline.
        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Pending, job!.State);
        Assert.Equal(JobRt, job.RtId);

        // Source min 10:15 snaps down to the 10:00 bucket boundary; the range ends at Now (14:00).
        var expectedFrom = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.State == RecomputeJobState.Pending && j.Trigger == RecomputeTrigger.Manual
                     && j.RangeStart == expectedFrom && j.RangeEnd == Now)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(RollupRt,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(
                    r => r.Count == 1 && r[0].RangeStart == expectedFrom && r[0].RangeEnd == Now)))
            .MustHaveHappenedOnceExactly();
        // The heavy executor must NOT run on the request path.
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_EmptySource_IsNoOp_ReturnsNullWithoutEnqueue()
    {
        StubRollupAndSource();
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._))
            .Returns(Coverage(null));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.Null(job);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_NotARollup_FailsWithoutResolvingSourceMin()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns((RollupArchiveSnapshot?)null);

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_NotActivated_FailsWithoutResolvingSourceMin()
    {
        // Backfilling a non-activated rollup must fail fast instead of pre-creating a Pending job +
        // range that the background drain silently skips forever.
        A.CallTo(() => _rollupStore.GetAsync(RollupRt))
            .Returns(Rollup(RollupRt, SourceRt, CkArchiveStatus.Disabled));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        Assert.Contains("not activated", job.ErrorReason!, StringComparison.OrdinalIgnoreCase);
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_WhenJobAlreadyActive_FoldsRangeIntoActiveJob()
    {
        StubRollupAndSource();
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc)));
        var activeJob = new RecomputeJobSnapshot(
            JobRt, RollupRt, RecomputeJobState.Running, RecomputeTrigger.Manual,
            From, To, null, null, null, Now, null, null, null, null);
        A.CallTo(() => _jobStore.GetActiveForArchiveAsync(RollupRt)).Returns(activeJob);

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        // The already-active job is handed back so the caller polls a single id — no second job created.
        Assert.NotNull(job);
        Assert.Equal(JobRt, job!.RtId);
        Assert.Equal(RecomputeJobState.Running, job.State);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(RollupRt, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Tick_AdoptsPreCreatedPendingJob_DrivesItToCompletedInPlace()
    {
        StubRollupAndSource();
        A.CallTo(() => _archiveStore.EnumerateAsync()).Returns(ToAsync(Array.Empty<ArchiveSnapshot>()));
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { Rollup(RollupRt, SourceRt) }));
        A.CallTo(() => _stateStore.GetPendingRecomputeRangesAsync(RollupRt))
            .Returns((IReadOnlyList<ArchiveRecomputeRange>)new[] { new ArchiveRecomputeRange(RollupRt, From, To, null, Now) });

        // A background backfill pre-created this Pending job; the tick must adopt it.
        var pendingJob = new RecomputeJobSnapshot(
            JobRt, RollupRt, RecomputeJobState.Pending, RecomputeTrigger.Manual,
            From, To, null, null, null, null, null, null, null, null);
        A.CallTo(() => _jobStore.GetActiveForArchiveAsync(RollupRt)).Returns(pendingJob);

        var count = await NewSut().TickAsync(CancellationToken.None);

        Assert.Equal(1, count);
        // Adopted job is advanced in place (UpdateAsync), never re-created (CreateAsync).
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _jobStore.UpdateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.RtId == JobRt && j.State == RecomputeJobState.Running))).MustHaveHappened();
        A.CallTo(() => _jobStore.UpdateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.RtId == JobRt && j.State == RecomputeJobState.Completed))).MustHaveHappened();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, From, To, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ---- TickAsync ----------------------------------------------------------------------------

    [Fact]
    public async Task Tick_DrainsPendingRangesForActivatedRollup()
    {
        StubRollupAndSource();
        A.CallTo(() => _archiveStore.EnumerateAsync()).Returns(ToAsync(Array.Empty<ArchiveSnapshot>()));
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { Rollup(RollupRt, SourceRt) }));
        A.CallTo(() => _stateStore.GetPendingRecomputeRangesAsync(RollupRt)).Returns((IReadOnlyList<ArchiveRecomputeRange>)new[]
        {
            new ArchiveRecomputeRange(RollupRt, From, To, null, Now),
        });

        var count = await NewSut().TickAsync(CancellationToken.None);

        Assert.Equal(1, count);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, From, To, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // AB#5189: the due ranges are consumed by a single Replace that keeps the backed-off ones,
        // not by a blanket Clear — here nothing is held back, so the replacement list is empty.
        A.CallTo(() => _stateStore.ReplacePendingRecomputeRangesAsync(
                RollupRt, A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs => rs.Count == 0)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Tick_SkipsNonActivatedRollup()
    {
        A.CallTo(() => _archiveStore.EnumerateAsync()).Returns(ToAsync(Array.Empty<ArchiveSnapshot>()));
        A.CallTo(() => _rollupStore.EnumerateAsync())
            .Returns(ToAsync(new[] { Rollup(RollupRt, SourceRt, CkArchiveStatus.Disabled) }));

        var count = await NewSut().TickAsync(CancellationToken.None);

        Assert.Equal(0, count);
        A.CallTo(() => _stateStore.GetPendingRecomputeRangesAsync(RollupRt)).MustNotHaveHappened();
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    // ---- AB#4336 decision D2: TWA carry extension --------------------------------------------

    private static RollupArchiveSnapshot TwaRollup(
        OctoObjectId rtId, OctoObjectId sourceRtId, DateTime? lastAggregatedBucketEnd) =>
        TwaRollup(rtId, new[] { new RollupSourceReference(sourceRtId) }, lastAggregatedBucketEnd);

    private static RollupArchiveSnapshot TwaRollup(
        OctoObjectId rtId, IReadOnlyList<RollupSourceReference> sources, DateTime? lastAggregatedBucketEnd) =>
        new(rtId, TargetType, CkArchiveStatus.Activated, null, sources,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), lastAggregatedBucketEnd,
            new[] { new CkRollupAggregationSpec("dimmingLevel", CkRollupFunction.TimeWeightedAvg, null) }, null);

    [Fact]
    public async Task Propagate_RetroactiveWindow_TwaDependent_ExtendsRangeByOneBucket()
    {
        // A retroactive change inside [10:00, 11:00) also changes the LOCF carry-in of the NEXT
        // bucket — the enqueued range must reach 12:00, not 11:00.
        var child = TwaRollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Propagate_RetroactiveWindow_TwaDependent_ExtensionStillClampedToWatermark()
    {
        // The successor bucket has not been aggregated yet (watermark 11:00) — the extension is
        // clamped back; forward aggregation will derive the fresh carry for [11:00, 12:00) itself.
        var child = TwaRollup(OctoObjectId.GenerateNewId(), SourceRt,
            lastAggregatedBucketEnd: new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc));
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt)).Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
        {
            new ArchiveDirtyWindow(
                new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc),
                RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now),
        });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }
    // ---- AB#5157: multi-source recompute ------------------------------------------------------

    private static readonly OctoObjectId NativeRt = OctoObjectId.GenerateNewId();

    /// The legacy/native cutover; on the hourly bucket grid of every rollup built here.
    private static readonly DateTime Cutover = new(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveCoverageInvalidator _coverageInvalidator = A.Fake<IArchiveCoverageInvalidator>();

    private RecomputeOrchestrator NewSutWithInvalidator() =>
        new(TenantId, _archiveStore, _rollupStore, _graph, _stateStore, _jobStore, _executor, _streamData, _audit,
            NullLogger<RecomputeOrchestrator>.Instance, () => Now,
            RecomputeOrchestrator.DefaultMaxBucketsPerChunk,
            RecomputeOrchestrator.DefaultMaxChunkAttempts,
            chunkRetryBaseDelay: TimeSpan.Zero,
            delay: (_, _) => Task.CompletedTask,
            coverageInvalidator: _coverageInvalidator);

    private static ArchiveSnapshot NativeSource() =>
        new(NativeRt, new RtCkId<CkTypeId>("Test", new CkTypeId("TempSensor")),
            CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    private static RollupSourceReference[] CutoverSources() => new[]
    {
        new RollupSourceReference(SourceRt, ValidTo: Cutover),
        new RollupSourceReference(NativeRt, ValidFrom: Cutover),
    };

    private void StubBothSources()
    {
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Source());
        A.CallTo(() => _archiveStore.GetAsync(NativeRt)).Returns(NativeSource());
    }

    private static ArchiveDirtyWindow RetroWindow(DateTime start, DateTime end) =>
        new(start, end, RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, Now);

    // A rollup that lists the changed archive as ONE of several sources is a direct child of it.
    [Fact]
    public async Task Propagate_DependentListingTheSourceAmongTwo_IsADirectChild()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }

    // TC-REC-05: a retroactive write into the legacy source shortly before the cutover produces a
    // dirty window reaching past it; the enqueued range stops at the span end because the buckets
    // from the cutover on are served by the other source.
    [Fact]
    public async Task Propagate_RetroactiveLegacyWrite_ClipsTheEnqueuedRangeAtTheCutover()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(Cutover - TimeSpan.FromMinutes(30), Cutover + TimeSpan.FromMinutes(30)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        // Unclipped the aligned window would have been [11:00, 13:00).
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == Cutover - TimeSpan.FromHours(1)
                    && rs[0].RangeEnd == Cutover)))
            .MustHaveHappenedOnceExactly();
    }

    // TC-REC-06: a write into a source outside its own validity span makes nothing stale.
    [Fact]
    public async Task Propagate_WriteOutsideTheSourcesSpan_EnqueuesNothing()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(Cutover + TimeSpan.FromMinutes(30), Cutover + TimeSpan.FromMinutes(90)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // The AB#4336 TWA carry extension must not reach into the successor bucket when that bucket is
    // served by another source — its carry-in comes from that source's own rows.
    [Fact]
    public async Task Propagate_TwaDependent_CarryExtensionStopsAtTheSpanEnd()
    {
        var spanEnd = new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc);
        var child = TwaRollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: spanEnd),
                new RollupSourceReference(NativeRt, ValidFrom: spanEnd),
            },
            lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        // Without the span cap the TWA extension would have reached 12:00.
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == spanEnd)))
            .MustHaveHappenedOnceExactly();
    }

    // TC-X-REC-02: a rung reachable from the changed archive both directly and through another
    // rollup is enqueued exactly once.
    [Fact]
    public async Task Propagate_DependentReachableThroughTwoPaths_IsEnqueuedOnce()
    {
        var intermediate = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        var diamond = Rollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: Cutover),
                new RollupSourceReference(intermediate.RtId, ValidFrom: Cutover),
            },
            lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { intermediate, diamond });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 11, 10, 45, 0, DateTimeKind.Utc)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                diamond.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                intermediate.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
    }

    // The recompute is split along the validity spans: one executor call per source segment, with
    // that segment's source snapshot; the job totals accumulate over the segments.
    [Fact]
    public async Task Recompute_RangeSpanningTheCutover_RunsOneExecutorCallPerSourceSegment_AndSumsTotals()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, CutoverSources()));
        StubBothSources();
        var from = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc);

        var job = await NewSut().RecomputeArchiveAsync(
            RollupRt, from, to, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.Equal(84, job.RowsProcessed);    // two segments × (42, 3)
        Assert.Equal(6, job.WindowsProcessed);
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt), A<RollupArchiveSnapshot>._,
                from, Cutover, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == NativeRt), A<RollupArchiveSnapshot>._,
                Cutover, to, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task Recompute_RangeCoveredByNoSpan_CompletesWithoutRunningTheExecutor()
    {
        // A deliberate gap: the legacy source ends at 09:00, the native one starts at 13:00.
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc)),
            new RollupSourceReference(NativeRt, ValidFrom: new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc)),
        }));
        StubBothSources();

        var job = await NewSut().RecomputeArchiveAsync(
            RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.Equal(0, job.RowsProcessed);
        Assert.Equal(0, job.WindowsProcessed);
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_Completed_InvalidatesTheMemoisedCoverageOfTheRollup()
    {
        StubRollupAndSource();

        await NewSutWithInvalidator().RecomputeArchiveAsync(
            RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        A.CallTo(() => _coverageInvalidator.Invalidate(TenantId, RollupRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_Failed_DoesNotInvalidateTheMemoisedCoverage()
    {
        StubRollupAndSource();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("column does not exist"));

        await NewSutWithInvalidator().RecomputeArchiveAsync(
            RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        A.CallTo(() => _coverageInvalidator.Invalidate(A<string>._, A<OctoObjectId?>._)).MustNotHaveHappened();
    }

    // TC-AGG-09: the backfill start is the minimum over all sources, not the newest source's start.
    [Fact]
    public async Task Backfill_StartIsTheMinimumOverAllSources()
    {
        var nativeFrom = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, new[]
        {
            new RollupSourceReference(SourceRt),
            new RollupSourceReference(NativeRt, ValidFrom: nativeFrom),
        }));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(NativeRt, A<CancellationToken>._))
            .Returns(Coverage(nativeFrom));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.RangeStart == new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) && j.RangeEnd == Now)))
            .MustHaveHappenedOnceExactly();
    }

    // TC-AGG-10: a source's earliest stored timestamp is raised to its ValidFrom before the minimum
    // is taken — data it holds outside its span is never read by the rollup.
    [Fact]
    public async Task Backfill_SourceEarliestTimestampIsRaisedToItsValidFrom()
    {
        var nativeFrom = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var legacyMin = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, new[]
        {
            new RollupSourceReference(SourceRt),
            new RollupSourceReference(NativeRt, ValidFrom: nativeFrom),
        }));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._)).Returns(Coverage(legacyMin));
        // The native archive holds older rows than its span allows; they must not lower the start.
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(NativeRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(j => j.RangeStart == legacyMin)))
            .MustHaveHappenedOnceExactly();
    }

    // TC-AGG-11 / TC-X-AGG-02: an empty source is skipped without error.
    [Fact]
    public async Task Backfill_SourceHoldingNoData_ContributesNothingAndDoesNotBlockTheBackfill()
    {
        var legacyMin = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, CutoverSources()));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._)).Returns(Coverage(legacyMin));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(NativeRt, A<CancellationToken>._))
            .Returns(Coverage(null));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Pending, job!.State);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(j => j.RangeStart == legacyMin)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Backfill_SourceWhoseDataLiesEntirelyAfterItsValidTo_ContributesNothing()
    {
        var legacyMin = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, new[]
        {
            new RollupSourceReference(SourceRt),
            new RollupSourceReference(NativeRt, ValidTo: new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
        }));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._)).Returns(Coverage(legacyMin));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(NativeRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc)));

        await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(j => j.RangeStart == legacyMin)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Backfill_NoSourceHoldsInSpanData_IsANoOp()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, CutoverSources()));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .Returns(Coverage(null));

        var job = await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.Null(job);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    // TC-AGG-08: history is only produced on an explicit backfill — a freshly activated
    // multi-source rollup queues nothing by itself.
    [Fact]
    public async Task Tick_ActivatedMultiSourceRollupWithoutPendingWork_QueuesNoBackfill()
    {
        StubBothSources();
        A.CallTo(() => _archiveStore.EnumerateAsync()).Returns(ToAsync(Array.Empty<ArchiveSnapshot>()));
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(new[] { Rollup(RollupRt, CutoverSources()) }));

        var count = await NewSut().TickAsync(CancellationToken.None);

        Assert.Equal(0, count);
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>._)).MustNotHaveHappened();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ---- AB#5157 follow-up E2: single-timestamp (degenerate) dirty windows --------------------

    // The retroactive detector persists a single-timestamp correction as [t, t + 1 tick); Mongo's
    // millisecond resolution collapses it to [t, t). It must still mark the bucket holding t stale on
    // a multi-source dependent — the span clip alone would discard the empty window.
    [Fact]
    public async Task Propagate_DegenerateWindowInsideTheSpan_EnqueuesExactlyOneBucket()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        var point = new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc);
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[] { RetroWindow(point, point) });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // The same shape on a single unbounded source — the pre-AB#5157 rollup, where the regression showed.
    [Fact]
    public async Task Propagate_DegenerateWindow_SingleUnboundedSource_EnqueuesExactlyOneBucket()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt, lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        var point = new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc);
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[] { RetroWindow(point, point) });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }

    // A point exactly on a bucket boundary inside the span marks the bucket STARTING there, not the one before.
    [Fact]
    public async Task Propagate_DegenerateWindowOnABucketBoundary_EnqueuesTheBucketStartingThere()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        var boundary = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[] { RetroWindow(boundary, boundary) });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == boundary
                    && rs[0].RangeEnd == boundary + TimeSpan.FromHours(1))))
            .MustHaveHappenedOnceExactly();
    }

    // A point exactly at ValidTo lies outside the half-open span: nothing becomes stale.
    [Fact]
    public async Task Propagate_DegenerateWindowAtValidTo_EnqueuesNothing()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[] { RetroWindow(Cutover, Cutover) });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
        A.CallTo(() => _stateStore.ClearDirtyWindowsAsync(SourceRt)).MustHaveHappenedOnceExactly();
    }

    // A point one tick before ValidTo is the last instant inside the span: the final bucket is stale.
    [Fact]
    public async Task Propagate_DegenerateWindowOneTickBeforeValidTo_EnqueuesTheLastBucketOfTheSpan()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        var point = Cutover.AddTicks(-1);
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[] { RetroWindow(point, point) });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == Cutover - TimeSpan.FromHours(1)
                    && rs[0].RangeEnd == Cutover)))
            .MustHaveHappenedOnceExactly();
    }

    // A normal window is not widened: one ending exactly on a bucket boundary still yields exactly
    // that one bucket (a blanket +1 tick would drag in the next bucket).
    [Fact]
    public async Task Propagate_NormalWindowEndingOnABoundary_IsNotWidened()
    {
        var child = Rollup(OctoObjectId.GenerateNewId(), CutoverSources(), lastAggregatedBucketEnd: Now);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(SourceRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { child });
        A.CallTo(() => _stateStore.GetDirtyWindowsAsync(SourceRt))
            .Returns((IReadOnlyList<ArchiveDirtyWindow>)new[]
            {
                RetroWindow(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc)),
            });

        await NewSut().PropagateDirtyWindowsAsync(SourceRt, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                child.RtId,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)
                    && rs[0].RangeEnd == new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }

    // ---- AB#5189 item A: an interrupted recompute must record what it did not finish ----------

    // Item B: the forward pass owns every bucket that is not lag-closed yet, so a recompute reaching
    // up to "now" must stop at `now - WatermarkLag`, not at the bucket containing `now`. The harness
    // rollup has a 5-minute lag and Now is 14:00, so 13:00 is the last bucket the recompute may claim.
    [Fact]
    public async Task Recompute_RangeReachingUpToNow_StopsAtTheLastLagClosedBucket()
    {
        StubRollupAndSource();
        var from = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);

        var job = await NewSut().RecomputeArchiveAsync(
            RollupRt, from, Now, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                from, new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc), null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // Nothing may touch the bucket the forward pass is still refreshing.
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>.That.IsGreaterThan(new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc)),
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Recompute_WholeRangeInsideTheLagWindow_IsANoOp()
    {
        StubRollupAndSource();

        var job = await NewSut().RecomputeArchiveAsync(
            RollupRt, new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc), Now, null,
            RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        // No obligation is parked either: the forward pass rewrites those buckets from the source on
        // every tick until they lag-close, so the correction lands through that path.
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    // ---- AB#5189: drain attempt bookkeeping ---------------------------------------------------

    private void StubPending(params ArchiveRecomputeRange[] ranges)
    {
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsyncEnum(Rollup(RollupRt, SourceRt)));
        A.CallTo(() => _archiveStore.EnumerateAsync())
            .Returns(ToAsyncEnum(Array.Empty<ArchiveSnapshot>()));
        A.CallTo(() => _stateStore.GetPendingRecomputeRangesAsync(RollupRt))
            .Returns((IReadOnlyList<ArchiveRecomputeRange>)ranges);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnum<T>(params T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    [Fact]
    public async Task Tick_FailedInterval_ReEnqueuesOnlyTheRemainder_WithAttemptAndBackoff()
    {
        StubRollupAndSource();
        var start = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 5, 11, 5, 0, 0, DateTimeKind.Utc);
        var failedChunkStart = new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc);
        StubPending(new ArchiveRecomputeRange(RollupRt, start, end, null, Now));

        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                failedChunkStart, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));

        await NewSut(maxBucketsPerChunk: 2).TickAsync(CancellationToken.None);

        // Only the tail is re-enqueued — the committed prefix is not repeated on the next tick, which
        // is what made a permanently failing rollup re-commit the same buckets every minute.
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(RollupRt,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1
                    && rs[0].RangeStart == failedChunkStart
                    && rs[0].RangeEnd == end
                    && rs[0].Attempts == 1
                    && rs[0].NextAttemptAt == Now + RecomputeOrchestrator.DefaultRangeRetryBaseDelay
                    && rs[0].LastError == "boom")))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Tick_RangeAtTheAttemptCap_IsGivenUpOnInsteadOfRetriedForever()
    {
        StubRollupAndSource();
        var start = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc);
        // One short of the cap: this run's failure takes it to the cap.
        StubPending(new ArchiveRecomputeRange(
            RollupRt, start, end, null, Now, RecomputeOrchestrator.DefaultMaxRangeAttempts - 1));

        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("permanently broken"));

        await NewSut().TickAsync(CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                RollupRt, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_BackedOffRange_IsNotRunAndIsKept()
    {
        StubRollupAndSource();
        var start = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc);
        StubPending(new ArchiveRecomputeRange(
            RollupRt, start, end, null, Now, 1, Now.AddMinutes(10), "earlier failure"));

        var runs = await NewSut().TickAsync(CancellationToken.None);

        Assert.Equal(0, runs);
        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        // Crucially the obligation must still be there — neither cleared nor replaced away.
        A.CallTo(() => _stateStore.ClearPendingRecomputeRangesAsync(RollupRt)).MustNotHaveHappened();
        A.CallTo(() => _stateStore.ReplacePendingRecomputeRangesAsync(
                RollupRt, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_DueRangeConsumed_KeepsTheBackedOffOnesInOneWrite()
    {
        StubRollupAndSource();
        var dueRange = new ArchiveRecomputeRange(
            RollupRt, new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc), null, Now);
        var backedOff = new ArchiveRecomputeRange(
            RollupRt, new DateTime(2026, 5, 11, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 11, 4, 0, 0, DateTimeKind.Utc), null, Now, 2, Now.AddMinutes(5), "later");
        StubPending(dueRange, backedOff);

        await NewSut().TickAsync(CancellationToken.None);

        A.CallTo(() => _stateStore.ReplacePendingRecomputeRangesAsync(RollupRt,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Count == 1 && rs[0].RangeStart == backedOff.RangeStart && rs[0].Attempts == 2)))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.ClearPendingRecomputeRangesAsync(RollupRt)).MustNotHaveHappened();
    }

    // ---- AB#5189: backfill must skip a source that cannot serve a single bucket ----------------

    [Fact]
    public async Task Backfill_SourceWhoseDataEndsBeforeItsValidFrom_DoesNotDragTheStartBack()
    {
        var staleFrom = new DateTime(2026, 5, 11, 8, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns(Rollup(RollupRt, new[]
        {
            // Declared from 08:00 but holding nothing after 07:00 — it can serve no bucket at all.
            new RollupSourceReference(SourceRt, ValidFrom: staleFrom),
            new RollupSourceReference(NativeRt, ValidFrom: new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc)),
        }));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(SourceRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2026, 5, 11, 6, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 11, 7, 0, 0, DateTimeKind.Utc)));
        A.CallTo(() => _streamData.GetArchiveCoverageAsync(NativeRt, A<CancellationToken>._))
            .Returns(Coverage(new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc)));

        await NewSut().EnqueueBackfillFromSourceAsync(RollupRt, CancellationToken.None);

        // Without the skip the start would be dragged back to the stale source's ValidFrom (08:00)
        // and the backfill would grind over four hours of buckets nothing can fill.
        A.CallTo(() => _jobStore.CreateAsync(A<RecomputeJobSnapshot>.That.Matches(
                j => j.RangeStart == new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc))))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Recompute_MidChunkFailure_RecordsTheUnfinishedRemainderAsOutstanding()
    {
        // Same shape as Recompute_LargeRange_MidChunkFailure_LeavesPriorChunksCommitted, but asserting
        // the other half of the contract: the chunks that never ran must be recoverable. Without that
        // record the range stays half recomputed — the first chunk carries fresh values, the rest keep
        // their old ones — and only a later, unrelated dirty write would ever finish the job.
        StubRollupAndSource();
        var from = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 11, 5, 0, 0, DateTimeKind.Utc);
        var failedChunkStart = new DateTime(2026, 5, 11, 2, 0, 0, DateTimeKind.Utc);

        A.CallTo(() => _executor.ExecuteAsync(A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                failedChunkStart, A<DateTime>._, A<OctoObjectId?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("chunk 2 exploded"));

        var job = await NewSut(maxBucketsPerChunk: 2)
            .RecomputeArchiveAsync(RollupRt, from, to, null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Failed, job.State);

        // Fix-neutral: whatever the bookkeeping looks like, the un-run tail [02:00, 05:00) must be
        // covered by pending ranges on THIS rollup, so a later pass finishes it without an operator.
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(
                RollupRt,
                A<IReadOnlyList<ArchiveRecomputeRange>>.That.Matches(rs =>
                    rs.Any(r => r.RangeStart <= failedChunkStart && r.RangeEnd >= to))))
            .MustHaveHappened();
    }
}
