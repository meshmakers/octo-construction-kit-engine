using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#8002: adding a computed column or changing its formula queues the backfill as a job instead of
/// running it inside the request. These tests drive the queued path end to end over in-memory stores:
/// the call returns at once, the drain runs the job, a request cancellation cannot reach it, several
/// jobs queue, a failure is recorded and can be retried, a shutdown hands the job back.
/// </summary>
public class ComputedColumnBackfillJobTests
{
    private const string TenantId = "tenant-x";
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("EnergyMeter"));

    private readonly OctoObjectId _rt = OctoObjectId.GenerateNewId();
    private readonly InMemoryArchiveRuntimeStore _store = new();
    private readonly InMemoryRecomputeJobStore _jobs = new();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    public ComputedColumnBackfillJobTests()
    {
        A.CallTo(() => _repo.NormalizeComputedFormulaAsync(
                A<OctoObjectId>._, A<IReadOnlyList<CkArchiveColumnSpec>>._, A<string>._, A<CancellationToken>._))
            .ReturnsLazily((OctoObjectId _, IReadOnlyList<CkArchiveColumnSpec> _, string f, CancellationToken _) => f);
        _store.Put(new ArchiveSnapshot(_rt, TargetType, CkArchiveStatus.Activated, "grid", new List<CkArchiveColumnSpec>()));
    }

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, A.Fake<IArchiveAuditTrail>(), NullLogger<ArchiveLifecycleService>.Instance,
            recomputeJobStore: _jobs, clock: () => _now);

    private static CkArchiveColumnSpec Active(string name, string formula = "activepower * -1") =>
        new(string.Empty, Indexed: true, Required: false)
        {
            Name = name, Formula = formula, ResultType = FormulaResultType.Double, ComputedState = ComputedColumnState.Active,
        };

    private void AddColumn(CkArchiveColumnSpec column) => _store.AddComputedColumnAsync(_rt, column).GetAwaiter().GetResult();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<RecomputeJobSnapshot?> EnqueueAdd(ArchiveLifecycleService sut, string name, CancellationToken? token = null) =>
        sut.EnqueueAddComputedColumnAsync(_rt, name, "activepower * -1", FormulaResultType.Double, indexed: true, token ?? Ct);

    private void BackfillReports(long rows) =>
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, string _, IProgress<long>? progress, CancellationToken _) => progress?.Report(rows));

    [Fact]
    public async Task EnqueueAdd_ReturnsAtOnce_ColumnBackfilling_JobPending_NoBackfillInTheCall()
    {
        var job = await EnqueueAdd(NewSut(), "ExportPower");

        Assert.NotNull(job);
        Assert.Equal(RecomputeJobState.Pending, job!.State);
        Assert.Equal(RecomputeTrigger.Manual, job.Trigger);
        Assert.Equal("ExportPower", job.ComputedColumnName);
        Assert.Equal(_rt, job.ArchiveRtId);
        Assert.Equal(ComputedColumnState.Backfilling, _store.Column(_rt, "ExportPower")!.ComputedState);
        A.CallTo(() => _repo.AddComputedColumnStorageAsync(A<ArchiveSnapshot>._, "ExportPower", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Drain_RunsTheQueuedJob_ColumnActive_JobCompletedWithRows()
    {
        var sut = NewSut();
        var queued = await EnqueueAdd(sut, "ExportPower");
        BackfillReports(748_000);

        var completed = await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(1, completed);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ExportPower")!.ComputedState);
        var job = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Completed, job!.State);
        Assert.Equal(748_000, job.RowsProcessed);
        Assert.NotNull(job.FinishedAt);
        // The caller polls one job id from Pending through Running to Completed.
        Assert.Equal(
            new[] { RecomputeJobState.Pending, RecomputeJobState.Running, RecomputeJobState.Completed },
            _jobs.History.Where(h => h.RtId == queued.RtId).Select(h => h.State).Distinct());
    }

    [Fact]
    public async Task RequestCancellation_DoesNotReachTheBackfill()
    {
        var sut = NewSut();
        using var request = new CancellationTokenSource();
        var queued = await EnqueueAdd(sut, "ExportPower", request.Token);

        // The client gives up (timeout, disconnect) right after the call returned.
        await request.CancelAsync();
        CancellationToken seen = default;
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, string _, IProgress<long>? _, CancellationToken token) => seen = token);

        using var host = new CancellationTokenSource();
        await sut.RunPendingComputedColumnBackfillsAsync(host.Token);

        Assert.False(seen.IsCancellationRequested);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ExportPower")!.ComputedState);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(queued!.RtId))!.State);
    }

    [Fact]
    public async Task TwoBackfills_AreQueued_AndRunOneAfterTheOther_OldestFirst()
    {
        var sut = NewSut();
        var first = await EnqueueAdd(sut, "ExportPower");
        var second = await EnqueueAdd(sut, "ImportPower");

        var running = 0;
        var maxParallel = 0;
        var order = new List<string>();
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .ReturnsLazily(async (ArchiveSnapshot _, string column, IProgress<long>? _, CancellationToken _) =>
            {
                maxParallel = Math.Max(maxParallel, Interlocked.Increment(ref running));
                order.Add(column);
                await Task.Delay(20, Ct);
                Interlocked.Decrement(ref running);
            });

        // Both stay queued until the drain runs; the second waits for the first.
        Assert.Equal(RecomputeJobState.Pending, (await _jobs.GetAsync(second!.RtId))!.State);
        var completed = await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(2, completed);
        Assert.Equal(1, maxParallel);
        Assert.Equal(new[] { "ExportPower", "ImportPower" }, order);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(first!.RtId))!.State);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(second.RtId))!.State);
    }

    [Fact]
    public async Task FailedBackfill_IsRecordedWithReason_AndRetriedWithOneCall()
    {
        var sut = NewSut();
        var queued = await EnqueueAdd(sut, "ExportPower");
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("XX000: Job killed"));

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(ComputedColumnState.Failed, _store.Column(_rt, "ExportPower")!.ComputedState);
        var failed = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Failed, failed!.State);
        Assert.Contains("ExportPower", failed.ErrorReason);
        Assert.Contains("XX000: Job killed", failed.ErrorReason);

        // Retry: one call, no remove + add.
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);
        var retry = await sut.RetryComputedColumnBackfillAsync(_rt, "ExportPower", Ct);

        Assert.NotEqual(queued.RtId, retry!.RtId);
        Assert.Equal(RecomputeJobState.Pending, retry.State);
        Assert.Equal(ComputedColumnState.Backfilling, _store.Column(_rt, "ExportPower")!.ComputedState);

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ExportPower")!.ComputedState);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(retry.RtId))!.State);
    }

    [Fact]
    public async Task Retry_OfAnActiveColumn_Throws()
    {
        AddColumn(Active("ExportPower"));

        await Assert.ThrowsAsync<ComputedColumnInvalidException>(() =>
            NewSut().RetryComputedColumnBackfillAsync(_rt, "ExportPower", Ct));
    }

    [Fact]
    public async Task Retry_OfAColumnStuckInBackfillingWithoutJob_QueuesAJob()
    {
        // The state the incident left behind: Backfilling, the request that ran it long gone.
        AddColumn(Active("ExportPower") with { ComputedState = ComputedColumnState.Backfilling });

        var job = await NewSut().RetryComputedColumnBackfillAsync(_rt, "ExportPower", Ct);

        Assert.Equal(RecomputeJobState.Pending, job!.State);
        Assert.Equal("ExportPower", job.ComputedColumnName);
    }

    [Fact]
    public async Task Drain_PicksUpAColumnStuckInBackfilling_WithAFreshJob()
    {
        AddColumn(Active("ExportPower") with { ComputedState = ComputedColumnState.Backfilling });

        var completed = await NewSut().RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(1, completed);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ExportPower")!.ComputedState);
        var job = Assert.Single(_jobs.All);
        Assert.Equal(RecomputeTrigger.Periodic, job.Trigger);
        Assert.Equal(RecomputeJobState.Completed, job.State);
    }

    [Fact]
    public async Task Shutdown_HandsTheJobBack_AndTheNextDrainFinishesIt()
    {
        var sut = NewSut();
        var queued = await EnqueueAdd(sut, "ExportPower");
        using var shutdown = new CancellationTokenSource();
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Invokes(() => shutdown.Cancel())
            .Throws(() => new OperationCanceledException(shutdown.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RunPendingComputedColumnBackfillsAsync(shutdown.Token));

        Assert.Equal(ComputedColumnState.Backfilling, _store.Column(_rt, "ExportPower")!.ComputedState);
        Assert.Equal(RecomputeJobState.Pending, (await _jobs.GetAsync(queued!.RtId))!.State);

        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Returns(Task.CompletedTask);
        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ExportPower")!.ComputedState);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(queued.RtId))!.State);
    }

    [Fact]
    public async Task Drain_LeavesALiveJobAlone_ButAdoptsAStaleOne()
    {
        AddColumn(Active("ExportPower") with { ComputedState = ComputedColumnState.Backfilling });
        var running = RecomputeJobSnapshot.ForComputedColumn(_rt, "ExportPower", RecomputeJobState.Running, RecomputeTrigger.Manual, _now);
        var runningId = await _jobs.CreateAsync(running);
        var sut = NewSut();

        // Another process heartbeats it: leave it alone.
        Assert.Equal(0, await sut.RunPendingComputedColumnBackfillsAsync(Ct));
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // Its process died: no heartbeat for longer than the stale timeout — the drain runs it again.
        _now += ComputedColumnBackfillRunner.DefaultStaleJobTimeout + TimeSpan.FromMinutes(1);
        Assert.Equal(1, await sut.RunPendingComputedColumnBackfillsAsync(Ct));
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(runningId))!.State);
    }

    [Fact]
    public async Task Drain_SkipsADisabledArchive()
    {
        await EnqueueAdd(NewSut(), "ExportPower");
        await _store.SetStatusAsync(_rt, CkArchiveStatus.Disabled);

        Assert.Equal(0, await NewSut().RunPendingComputedColumnBackfillsAsync(Ct));
        Assert.Equal(ComputedColumnState.Backfilling, _store.Column(_rt, "ExportPower")!.ComputedState);
    }

    [Fact]
    public async Task Disable_FailsTheQueuedBackfillJobs_ColumnStaysQueued()
    {
        var sut = NewSut();
        var queued = await EnqueueAdd(sut, "ExportPower");

        await sut.DisableAsync(_rt);

        var job = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        Assert.Contains("disabled", job.ErrorReason);
        Assert.Equal(ComputedColumnState.Backfilling, _store.Column(_rt, "ExportPower")!.ComputedState);
    }

    [Fact]
    public async Task ColumnRemovedDuringBackfill_FailsTheJob_AndDoesNotResurrectTheColumn()
    {
        var sut = NewSut();
        var queued = await EnqueueAdd(sut, "ExportPower");
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Invokes(() => _store.RemoveComputedColumnAsync(_rt, "ExportPower").GetAwaiter().GetResult());

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Null(_store.Column(_rt, "ExportPower"));
        var job = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        Assert.Contains("removed", job.ErrorReason);
    }

    [Fact]
    public async Task EnqueueFormulaChange_KeepsTheOldFormulaLive_UntilTheJobSwapsIt()
    {
        AddColumn(Active("ExportPower"));
        var sut = NewSut();

        var job = await sut.EnqueueUpdateComputedColumnFormulaAsync(_rt, "ExportPower", "activepower * -2", Ct);

        var queuedColumn = _store.Column(_rt, "ExportPower")!;
        Assert.Equal("activepower * -1", queuedColumn.Formula);
        Assert.Equal("activepower * -2", queuedColumn.PendingFormula);
        Assert.Equal(RecomputeJobState.Pending, job!.State);
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        var swapped = _store.Column(_rt, "ExportPower")!;
        Assert.Equal("activepower * -2", swapped.Formula);
        Assert.Equal(1, swapped.ComputedVersion);
        Assert.Null(swapped.PendingFormula);
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(job.RtId))!.State);
    }

    [Fact]
    public async Task FailedFormulaChange_RevertsToThePreviousFormula()
    {
        AddColumn(Active("ExportPower"));
        var sut = NewSut();
        var job = await sut.EnqueueUpdateComputedColumnFormulaAsync(_rt, "ExportPower", "activepower * -2", Ct);
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<IProgress<long>?>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("crate down"));

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        var column = _store.Column(_rt, "ExportPower")!;
        Assert.Equal("activepower * -1", column.Formula);
        Assert.Null(column.PendingFormula);
        Assert.Equal(ComputedColumnState.Active, column.ComputedState);
        Assert.Equal(RecomputeJobState.Failed, (await _jobs.GetAsync(job!.RtId))!.State);
    }

    [Fact]
    public async Task FormulaChange_WhileABackfillIsQueued_IsRejected()
    {
        var sut = NewSut();
        await EnqueueAdd(sut, "ExportPower");

        await Assert.ThrowsAsync<ComputedColumnInvalidException>(() =>
            sut.EnqueueUpdateComputedColumnFormulaAsync(_rt, "ExportPower", "activepower * -2", Ct));
    }

    [Fact]
    public async Task StorageFailure_OnEnqueue_IsThrown_AndLeavesTheColumnFailedWithoutAJob()
    {
        A.CallTo(() => _repo.AddComputedColumnStorageAsync(A<ArchiveSnapshot>._, A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("ddl"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => EnqueueAdd(NewSut(), "ExportPower"));

        Assert.Equal(ComputedColumnState.Failed, _store.Column(_rt, "ExportPower")!.ComputedState);
        Assert.Empty(_jobs.All);
    }

    [Fact]
    public void JobMarker_TellsABackfillJobFromARollupJob()
    {
        var backfill = RecomputeJobSnapshot.ForComputedColumn(_rt, "ExportPower", RecomputeJobState.Pending, RecomputeTrigger.Manual, _now);
        var rollup = backfill with { StagingTableName = "staging_abc" };

        Assert.True(backfill.IsComputedColumnBackfill);
        Assert.Equal("ExportPower", backfill.ComputedColumnName);
        Assert.Equal(DateTime.UnixEpoch, backfill.RangeStart);
        Assert.Equal(_now, backfill.RangeEnd);
        Assert.False(rollup.IsComputedColumnBackfill);
        Assert.Null(rollup.ComputedColumnName);
        Assert.False((backfill with { StagingTableName = null }).IsComputedColumnBackfill);
    }
}
