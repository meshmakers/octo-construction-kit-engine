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
/// AB#8331: an aborted formula evaluation left NULL cells in an active computed column (prod-1 energyiq).
/// <c>RepairComputedColumnAsync</c> queues a repair job on the existing recompute-job mechanism; the backfill
/// drain runs it. The column stays Active; the job records rows, checkpoint and failures.
/// </summary>
public class ComputedColumnRepairJobTests
{
    private const string TenantId = "tenant-x";
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("GridConnection"));

    private readonly OctoObjectId _rt = OctoObjectId.GenerateNewId();
    private readonly InMemoryArchiveRuntimeStore _store = new();
    private readonly InMemoryRecomputeJobStore _jobs = new();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly DateTime _now = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    public ComputedColumnRepairJobTests()
    {
        _store.Put(new ArchiveSnapshot(_rt, TargetType, CkArchiveStatus.Activated, "grid", new List<CkArchiveColumnSpec>()));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ArchiveLifecycleService NewSut() =>
        new(TenantId, _store, _repo, A.Fake<IArchiveAuditTrail>(), NullLogger<ArchiveLifecycleService>.Instance,
            recomputeJobStore: _jobs, clock: () => _now);

    private void AddColumn(string name, ComputedColumnState state) =>
        _store.AddComputedColumnAsync(_rt, new CkArchiveColumnSpec(string.Empty, Indexed: true, Required: false)
        {
            Name = name,
            Formula = "if(activepower > 0, activepower, 0)",
            ResultType = FormulaResultType.Double,
            ComputedState = state,
        }).GetAwaiter().GetResult();

    private void RepairReports(long rows, DateTime checkpoint) =>
        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<DateTime>._, A<DateTime>._,
                A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, string _, DateTime _, DateTime _, IProgress<ComputedColumnBackfillProgress>? progress, CancellationToken _) =>
                progress?.Report(new ComputedColumnBackfillProgress(rows, checkpoint)));

    [Fact]
    public async Task Repair_QueuesAPendingRepairJob_ColumnStaysActive_NothingRunsInTheCall()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);

        var job = await NewSut().RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);

        Assert.NotNull(job);
        Assert.True(job!.IsComputedColumnRepair);
        Assert.True(job.IsComputedColumnBackfill); // a computed-column job: kept away from the rollup orchestrator
        Assert.Equal("ImportPower", job.ComputedColumnName);
        Assert.Equal(RecomputeJobState.Pending, job.State);
        Assert.Equal(DateTime.UnixEpoch, job.RangeStart);
        Assert.Equal(_now, job.RangeEnd);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ImportPower")!.ComputedState);
        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<DateTime>._, A<DateTime>._,
            A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Drain_RunsTheRepairOverTheRequestedRange_JobCompleted_ColumnStillActive()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);
        var from = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc);
        var sut = NewSut();
        var queued = await sut.RepairComputedColumnAsync(_rt, "ImportPower", from, to, Ct);
        RepairReports(3, to);

        var completed = await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(1, completed);
        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, "ImportPower", from, to,
            A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.BackfillComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<DateTime?>._,
            A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._)).MustNotHaveHappened();
        var job = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Completed, job!.State);
        Assert.Equal(3, job.RowsProcessed);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ImportPower")!.ComputedState);
    }

    [Fact]
    public async Task Repair_Again_WhileQueued_ReturnsTheQueuedJob()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);
        var sut = NewSut();

        var first = await sut.RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);
        var second = await sut.RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);

        Assert.Equal(first!.RtId, second!.RtId);
    }

    [Theory]
    [InlineData(ComputedColumnState.Backfilling)]
    [InlineData(ComputedColumnState.Failed)]
    public async Task Repair_OfAColumnThatIsNotActive_IsRefused(ComputedColumnState state)
    {
        AddColumn("ImportPower", state);

        await Assert.ThrowsAsync<ComputedColumnInvalidException>(
            () => NewSut().RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct));
    }

    [Fact]
    public async Task FailedRepair_RecordsTheUnrepairedRange_AndLeavesTheColumnActive()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);
        var sut = NewSut();
        var queued = await sut.RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);
        var checkpoint = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<DateTime>._, A<DateTime>._,
                A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, string _, DateTime _, DateTime _, IProgress<ComputedColumnBackfillProgress>? progress, CancellationToken _) =>
            {
                progress?.Report(new ComputedColumnBackfillProgress(500, checkpoint));
                throw new FormulaEvaluationException("x", "aborted by OutOfMemoryException");
            });

        var completed = await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        Assert.Equal(0, completed);
        var job = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Failed, job!.State);
        Assert.Equal(checkpoint, job.RangeStart);
        Assert.Contains("2026-07-11T00:00:00", job.ErrorReason);
        Assert.Contains("OutOfMemoryException", job.ErrorReason);
        Assert.Equal(ComputedColumnState.Active, _store.Column(_rt, "ImportPower")!.ComputedState);
    }

    [Fact]
    public async Task Shutdown_HandsTheRepairBack_AndTheNextDrainResumesAtTheCheckpoint()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);
        var sut = NewSut();
        var queued = await sut.RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);
        var checkpoint = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        using var host = new CancellationTokenSource();
        var calls = 0;
        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, A<string>._, A<DateTime>._, A<DateTime>._,
                A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._))
            .Invokes((ArchiveSnapshot _, string _, DateTime _, DateTime _, IProgress<ComputedColumnBackfillProgress>? progress, CancellationToken token) =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                {
                    return; // the resumed run completes
                }

                progress?.Report(new ComputedColumnBackfillProgress(10, checkpoint));
                host.Cancel();
                token.ThrowIfCancellationRequested();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RunPendingComputedColumnBackfillsAsync(host.Token));
        var handedBack = await _jobs.GetAsync(queued!.RtId);
        Assert.Equal(RecomputeJobState.Pending, handedBack!.State);
        Assert.Equal(checkpoint, handedBack.RangeStart);

        await sut.RunPendingComputedColumnBackfillsAsync(Ct);

        A.CallTo(() => _repo.RepairComputedColumnAsync(A<ArchiveSnapshot>._, "ImportPower", checkpoint, A<DateTime>._,
            A<IProgress<ComputedColumnBackfillProgress>?>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.Equal(RecomputeJobState.Completed, (await _jobs.GetAsync(queued.RtId))!.State);
    }

    [Fact]
    public async Task ARepairJob_IsNotTakenForAQueuedBackfill()
    {
        AddColumn("ImportPower", ComputedColumnState.Active);
        var sut = NewSut();
        await sut.RepairComputedColumnAsync(_rt, "ImportPower", cancellationToken: Ct);

        // A retry on an Active column is still refused: the repair job does not make it look queued.
        await Assert.ThrowsAsync<ComputedColumnInvalidException>(
            () => sut.RetryComputedColumnBackfillAsync(_rt, "ImportPower", Ct));
    }
}
