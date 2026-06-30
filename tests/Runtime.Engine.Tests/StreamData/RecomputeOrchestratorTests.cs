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

    private RecomputeOrchestrator NewSut() =>
        new(TenantId, _archiveStore, _rollupStore, _graph, _stateStore, _jobStore, _executor, _streamData, _audit,
            NullLogger<RecomputeOrchestrator>.Instance, () => Now);

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId rtId, OctoObjectId sourceRtId, CkArchiveStatus status = CkArchiveStatus.Activated) =>
        new(rtId, TargetType, status, null, sourceRtId,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null,
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
        var directChild = Rollup(OctoObjectId.GenerateNewId(), RollupRt);
        var grandChild = Rollup(OctoObjectId.GenerateNewId(), directChild.RtId);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(RollupRt))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)new[] { directChild, grandChild });

        await NewSut().RecomputeArchiveAsync(RollupRt, From, To, null, RecomputeTrigger.Manual, CancellationToken.None);

        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(directChild.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _stateStore.EnqueueRecomputeRangesAsync(grandChild.RtId, A<IReadOnlyList<ArchiveRecomputeRange>>._))
            .MustNotHaveHappened();
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
        var child = Rollup(OctoObjectId.GenerateNewId(), SourceRt);
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

    // ---- BackfillRollupFromSourceAsync (AB#4269) ----------------------------------------------

    [Fact]
    public async Task Backfill_ResolvesSourceMin_AlignsDownToBucket_AndRecomputesToNow()
    {
        StubRollupAndSource(); // bucket size = 1h, FixedSize alignment
        var sourceMin = new DateTime(2026, 5, 11, 10, 15, 0, DateTimeKind.Utc);
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(SourceRt, A<CancellationToken>._))
            .Returns(sourceMin);

        var job = await NewSut().BackfillRollupFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Completed, job!.State);
        // Source min 10:15 snaps down to the 10:00 bucket boundary; the range ends at Now (14:00).
        var expectedFrom = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, expectedFrom, Now, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Backfill_EmptySource_IsNoOp_ReturnsNullWithoutRunningExecutor()
    {
        StubRollupAndSource();
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(SourceRt, A<CancellationToken>._))
            .Returns((DateTime?)null);

        var job = await NewSut().BackfillRollupFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.Null(job);
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Backfill_NotARollup_FailsWithoutResolvingSourceMin()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt)).Returns((RollupArchiveSnapshot?)null);

        var job = await NewSut().BackfillRollupFromSourceAsync(RollupRt, CancellationToken.None);

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        A.CallTo(() => _streamData.GetArchiveMinTimestampAsync(A<OctoObjectId>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _executor.ExecuteAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._,
                A<OctoObjectId?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
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
