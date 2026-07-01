using System;
using System.Collections.Generic;
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

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId rtId, OctoObjectId sourceRtId, CkArchiveStatus status = CkArchiveStatus.Activated,
        DateTime? lastAggregatedBucketEnd = null) =>
        new(rtId, TargetType, status, null, sourceRtId,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), lastAggregatedBucketEnd,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null);

    private static ArchiveSnapshot Source() =>
        new(SourceRt, new RtCkId<CkTypeId>("Test", new CkTypeId("TempSensor")),
            CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

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
        // No chain propagation on failure.
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
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
        // No chain propagation on failure (no dependents were enqueued).
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(A<OctoObjectId>._, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
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
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(SourceRt, A<CancellationToken>._))
            .Returns(sourceMin);

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
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(SourceRt, A<CancellationToken>._))
            .Returns((DateTime?)null);

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
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_WhenJobAlreadyActive_FoldsRangeIntoActiveJob()
    {
        StubRollupAndSource();
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(SourceRt, A<CancellationToken>._))
            .Returns(new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc));
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
        A.CallTo(() => _stateStore.ClearPendingRecomputeRangesAsync(RollupRt)).MustHaveHappenedOnceExactly();
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
}
