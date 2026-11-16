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

/// <summary>
/// AB#9016: a late stream-data delivery recomputes the rollups only for the series it touched, once per debounce
/// interval, and the chained rollups inherit the scope. Driven through <see cref="RecomputeOrchestrator.TickAsync"/>
/// over stateful in-memory stores, so the debounce, the restart and the cascade are observed exactly as the drain
/// sees them.
/// </summary>
public class RecomputeLateDeliveryTests
{
    private const string TenantId = "tenant-late";
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("Meter"));

    // Raw source a01 and the ladder a02 (hourly) -> a03 (daily) -> a04 (daily, second cascade level).
    private static readonly OctoObjectId A01 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId A02 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId A03 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId A04 = OctoObjectId.GenerateNewId();

    private static readonly OctoObjectId M1 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId M2 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId M3 = OctoObjectId.GenerateNewId();

    // The late day of the example (2026-11-13) and "now" two days later.
    private static readonly DateTime LateDay = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly InMemoryRecomputeStateStore _stateStore = new();
    private readonly InMemoryRecomputeJobStore _jobStore = new();
    private readonly RecordingExecutor _executor = new();
    private DateTime _now = Start;

    public RecomputeLateDeliveryTests()
    {
        var rollups = new[]
        {
            Rollup(A02, A01, TimeSpan.FromHours(1), new DateTime(2026, 11, 15, 11, 0, 0, DateTimeKind.Utc)),
            Rollup(A03, A02, TimeSpan.FromDays(1), new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)),
            Rollup(A04, A03, TimeSpan.FromDays(1), new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)),
        };

        A.CallTo(() => _rollupStore.EnumerateAsync()).ReturnsLazily(() => ToAsync(rollups));
        foreach (var rollup in rollups)
        {
            A.CallTo(() => _rollupStore.GetAsync(rollup.RtId)).Returns(rollup);
        }

        A.CallTo(() => _archiveStore.EnumerateAsync()).ReturnsLazily(() => ToAsync(new[] { Archive(A01) }));
        foreach (var id in new[] { A01, A02, A03, A04 })
        {
            A.CallTo(() => _archiveStore.GetAsync(id)).Returns(Archive(id));
        }
    }

    private RecomputeOrchestrator NewSut(TimeSpan? debounce = null, int maxScopedRtIds = RecomputeLateDataOptions.DefaultMaxScopedRtIds) =>
        new(TenantId, _archiveStore, _rollupStore, new RollupDependencyGraph(_rollupStore), _stateStore, _jobStore,
            _executor, A.Fake<IStreamDataRepository>(), A.Fake<IArchiveAuditTrail>(),
            NullLogger<RecomputeOrchestrator>.Instance, () => _now,
            new RecomputeLateDataOptions
            {
                LateDataDebounce = debounce ?? RecomputeLateDataOptions.DefaultLateDataDebounce,
                MaxScopedRtIds = maxScopedRtIds,
            });

    private static RollupArchiveSnapshot Rollup(OctoObjectId rtId, OctoObjectId source, TimeSpan bucket, DateTime watermark) =>
        new(rtId, TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(source) },
            bucket, TimeSpan.FromMinutes(5), watermark,
            new[] { new CkRollupAggregationSpec("energy", CkRollupFunction.Sum, null) }, null);

    private static ArchiveSnapshot Archive(OctoObjectId rtId) =>
        new(rtId, TargetType, CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    private void LateDelivery(DateTime from, DateTime to, params OctoObjectId[] rtIds) =>
        _stateStore.Windows(A01).Add(new ArchiveDirtyWindow(
            from, to, RecomputeChangeKind.RetroactiveModify, RecomputeChangeSource.Pipeline, _now)
        {
            RtIds = RtIdSet.Of(rtIds),
        });

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private IReadOnlyList<RecomputeJobSnapshot> CompletedJobs(OctoObjectId rollup) =>
        _jobStore.All.Where(j => j.ArchiveRtId == rollup && j.State == RecomputeJobState.Completed).ToList();

    // ---- Scoped enqueue ---------------------------------------------------------------------

    [Fact]
    public async Task LateDelivery_IsEnqueuedWithExactlyItsRtIds_AndHeldForTheDebounce()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M2, M1);

        await NewSut().TickAsync(CancellationToken.None);

        var range = Assert.Single(_stateStore.Pending(A02));
        Assert.Equal(RtIdSet.Of(new[] { M1, M2 }), range.RtIdSet);
        Assert.Null(range.RtIdScope);
        Assert.Equal(LateDay.AddHours(8), range.RangeStart);
        Assert.Equal(LateDay.AddHours(9), range.RangeEnd);
        Assert.Equal(Start + RecomputeLateDataOptions.DefaultLateDataDebounce, range.NextAttemptAt);
        Assert.True(range.IsDebounced);
        Assert.Empty(_executor.Calls);
        Assert.Empty(_stateStore.Windows(A01));
    }

    // ---- Debounce / coalescing ----------------------------------------------------------------

    [Fact]
    public async Task LateDeliveriesWithinOneInterval_ProduceExactlyOneJobPerRollup_OverTheUnionOfTheirSeries()
    {
        var sut = NewSut();

        // Three late packets for the same day, a minute apart (the 14.11 example had one every ~78 s).
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(1);
        LateDelivery(LateDay.AddHours(10), LateDay.AddHours(11), M2);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(2);
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(10), M3, M1);
        await sut.TickAsync(CancellationToken.None);

        Assert.Empty(_executor.Calls);
        Assert.Equal(3, _stateStore.Pending(A02).Count);

        // The oldest obligation's debounce elapses: all three run together.
        _now = Start.AddMinutes(5);
        await sut.TickAsync(CancellationToken.None);

        var all = RtIdSet.Of(new[] { M1, M2, M3 });
        foreach (var rollup in new[] { A02, A03, A04 })
        {
            var job = Assert.Single(CompletedJobs(rollup));
            Assert.Equal(all, job.RtIdSet);
            Assert.Null(job.RtIdScope);
            Assert.Empty(_stateStore.Pending(rollup));
        }

        var a02 = Assert.Single(_executor.Calls, c => c.Rollup == A02);
        Assert.Equal(all, a02.RtIds);
        Assert.Equal(LateDay.AddHours(8), a02.From);
        Assert.Equal(LateDay.AddHours(11), a02.To);
        Assert.DoesNotContain(_executor.Calls, c => c.RtIds is null);
    }

    [Fact]
    public async Task LateDeliveriesInTwoIntervals_ProduceTwoJobs()
    {
        var sut = NewSut();
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(5);
        await sut.TickAsync(CancellationToken.None);

        _now = Start.AddMinutes(6);
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M2);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(11);
        await sut.TickAsync(CancellationToken.None);

        Assert.Equal(2, CompletedJobs(A02).Count);
        Assert.Equal(new[] { RtIdSet.Of(new[] { M1 }), RtIdSet.Of(new[] { M2 }) },
            _executor.Calls.Where(c => c.Rollup == A02).Select(c => c.RtIds));
    }

    [Fact]
    public async Task DebounceZero_RunsTheLateDeliveryOnTheSameTick()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);

        await NewSut(debounce: TimeSpan.Zero).TickAsync(CancellationToken.None);

        Assert.Single(CompletedJobs(A02));
        Assert.Equal(RtIdSet.Of(new[] { M1 }), Assert.Single(_executor.Calls, c => c.Rollup == A02).RtIds);
    }

    [Fact]
    public async Task Restart_KeepsTheHeldObligations_AndTheNewProcessRunsThemOnce()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);
        await NewSut().TickAsync(CancellationToken.None);

        // The process dies; the obligation lives on the archive entity (MongoDB), not in the orchestrator.
        _now = Start.AddMinutes(2);
        LateDelivery(LateDay.AddHours(9), LateDay.AddHours(10), M2);
        var restarted = NewSut();
        await restarted.TickAsync(CancellationToken.None);
        Assert.Empty(_executor.Calls);

        _now = Start.AddMinutes(5);
        await restarted.TickAsync(CancellationToken.None);

        var job = Assert.Single(CompletedJobs(A02));
        Assert.Equal(RtIdSet.Of(new[] { M1, M2 }), job.RtIdSet);
    }

    // ---- Cascade ------------------------------------------------------------------------------

    [Fact]
    public async Task Cascade_InheritsTheScope_OverTwoLevels_WithoutDebounce()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1, M2);
        var sut = NewSut();
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(5);
        await sut.TickAsync(CancellationToken.None);

        var scope = RtIdSet.Of(new[] { M1, M2 });
        var a03 = Assert.Single(_executor.Calls, c => c.Rollup == A03);
        var a04 = Assert.Single(_executor.Calls, c => c.Rollup == A04);
        Assert.Equal(scope, a03.RtIds);
        Assert.Equal(scope, a04.RtIds);
        // The daily rollups recompute the whole late day, for the two series only.
        Assert.Equal(LateDay, a03.From);
        Assert.Equal(LateDay.AddDays(1), a03.To);
        Assert.All(_jobStore.All.Where(j => j.ArchiveRtId != A02), j => Assert.Equal(scope, j.RtIdSet));
    }

    // ---- Threshold ----------------------------------------------------------------------------

    [Fact]
    public async Task MoreRtIdsThanTheThreshold_FallBackToTheUnscopedRecompute()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1, M2, M3);

        await NewSut(debounce: TimeSpan.Zero, maxScopedRtIds: 2).TickAsync(CancellationToken.None);

        var call = Assert.Single(_executor.Calls, c => c.Rollup == A02);
        Assert.Null(call.RtIds);
        Assert.Null(Assert.Single(CompletedJobs(A02)).RtIdSet);
        Assert.All(_executor.Calls, c => Assert.Null(c.RtIds));
    }

    [Fact]
    public async Task AUnionAboveTheThreshold_FallsBackToTheUnscopedRecompute()
    {
        var sut = NewSut(maxScopedRtIds: 2);
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1, M2);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(1);
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M3);
        await sut.TickAsync(CancellationToken.None);
        _now = Start.AddMinutes(5);
        await sut.TickAsync(CancellationToken.None);

        var call = Assert.Single(_executor.Calls, c => c.Rollup == A02);
        Assert.Null(call.RtIds);
    }

    [Fact]
    public async Task ThresholdZero_SwitchesTheScopedRecomputeOff()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);

        await NewSut(debounce: TimeSpan.Zero, maxScopedRtIds: 0).TickAsync(CancellationToken.None);

        Assert.Null(Assert.Single(_executor.Calls, c => c.Rollup == A02).RtIds);
    }

    // ---- Unscoped paths stay unscoped -----------------------------------------------------------

    [Fact]
    public async Task ManualWrite_IsNeitherScopedNorDebounced()
    {
        _stateStore.Windows(A01).Add(new ArchiveDirtyWindow(
            LateDay.AddHours(8), LateDay.AddHours(9), RecomputeChangeKind.RetroactiveModify,
            RecomputeChangeSource.Manual, _now)
        {
            RtIds = RtIdSet.Of(new[] { M1 }),
        });

        await NewSut().TickAsync(CancellationToken.None);

        Assert.Null(Assert.Single(_executor.Calls, c => c.Rollup == A02).RtIds);
    }

    [Fact]
    public async Task ManualRecompute_StaysUnscoped()
    {
        var job = await NewSut().RecomputeArchiveAsync(
            A02, LateDay, LateDay.AddDays(1), null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Completed, job.State);
        Assert.Null(job.RtIdSet);
        Assert.All(_executor.Calls, c => Assert.Null(c.RtIds));
    }

    [Fact]
    public async Task AnUnscopedObligation_AbsorbsTheOverlappingSetScopedOnes()
    {
        _stateStore.Pending(A02).Add(new ArchiveRecomputeRange(A02, LateDay, LateDay.AddDays(1), null, Start));
        _stateStore.Pending(A02).Add(new ArchiveRecomputeRange(A02, LateDay.AddHours(8), LateDay.AddHours(9), null, Start)
        {
            RtIdSet = RtIdSet.Of(new[] { M1 }),
        });

        await NewSut().TickAsync(CancellationToken.None);

        var call = Assert.Single(_executor.Calls, c => c.Rollup == A02);
        Assert.Null(call.RtIds);
        Assert.Empty(_stateStore.Pending(A02));
    }

    [Fact]
    public async Task APartlyCoveredSetObligation_StaysSetScoped_InsteadOfWideningToEverySeries()
    {
        // A one-hour unscoped obligation overlaps a whole-day set obligation for one series.
        _stateStore.Pending(A02).Add(new ArchiveRecomputeRange(A02, LateDay.AddHours(8), LateDay.AddHours(9), null, Start));
        _stateStore.Pending(A02).Add(new ArchiveRecomputeRange(A02, LateDay, LateDay.AddDays(1), null, Start)
        {
            RtIdSet = RtIdSet.Of(new[] { M1 }),
        });

        await NewSut().TickAsync(CancellationToken.None);

        var calls = _executor.Calls.Where(c => c.Rollup == A02).ToList();
        Assert.Contains(calls, c => c.RtIds is null && c.From == LateDay.AddHours(8) && c.To == LateDay.AddHours(9));
        Assert.Contains(calls, c => Equals(c.RtIds, RtIdSet.Of(new[] { M1 })) && c.From == LateDay && c.To == LateDay.AddDays(1));
        Assert.Empty(_stateStore.Pending(A02));
    }

    [Fact]
    public async Task ASetRefresh_ReleasesAParkedSingleEntityObligationOfOneOfItsSeries()
    {
        var parked = new ArchiveRecomputeRange(A02, LateDay.AddHours(8), LateDay.AddHours(9), M1, Start, 5,
            ArchiveRecomputeRange.ParkedUntil, "earlier failure");
        var parkedUnscoped = new ArchiveRecomputeRange(A02, LateDay.AddHours(8), LateDay.AddHours(9), null, Start, 5,
            ArchiveRecomputeRange.ParkedUntil, "earlier failure");
        _stateStore.Pending(A02).Add(parked);
        _stateStore.Pending(A02).Add(parkedUnscoped);
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1, M2);

        await NewSut(debounce: TimeSpan.Zero).TickAsync(CancellationToken.None);

        // The refresh of {M1, M2} repaired M1's parked obligation; the unscoped one needs every series and stays parked.
        Assert.DoesNotContain(parked, _stateStore.Pending(A02));
        Assert.Contains(parkedUnscoped, _stateStore.Pending(A02));
    }

    // ---- The active-job guard serialises the refresh with every other recompute ----------------

    [Fact]
    public async Task ARunningUnscopedRecompute_HoldsTheSetRefreshBack_UntilItIsDone()
    {
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);
        var sut = NewSut(debounce: TimeSpan.Zero);
        var running = await _jobStore.CreateAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, A02, RecomputeJobState.Running, RecomputeTrigger.Manual,
            LateDay, LateDay.AddDays(1), null, null, null, _now, null, null, null, null, _now));

        await sut.TickAsync(CancellationToken.None);

        Assert.DoesNotContain(_executor.Calls, c => c.Rollup == A02);
        Assert.Single(_stateStore.Pending(A02));

        var job = (await _jobStore.GetAsync(running))!;
        await _jobStore.UpdateAsync(job with { State = RecomputeJobState.Completed, FinishedAt = _now });
        await sut.TickAsync(CancellationToken.None);

        Assert.Equal(RtIdSet.Of(new[] { M1 }), Assert.Single(_executor.Calls, c => c.Rollup == A02).RtIds);
    }

    [Fact]
    public async Task AManualRecomputeDuringASetRefresh_IsCoalesced_NotRunConcurrently()
    {
        await _jobStore.CreateAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, A02, RecomputeJobState.Running, RecomputeTrigger.Periodic,
            LateDay.AddHours(8), LateDay.AddHours(9), null, null, null, _now, null, null, null, null, _now)
        {
            RtIdSet = RtIdSet.Of(new[] { M1 }),
        });

        var manual = await NewSut().RecomputeArchiveAsync(
            A02, LateDay, LateDay.AddDays(1), null, RecomputeTrigger.Manual, CancellationToken.None);

        Assert.Equal(RecomputeJobState.Coalesced, manual.State);
        Assert.Empty(_executor.Calls);
        // The manual range waits as an unscoped obligation for the refresh to finish.
        var queued = Assert.Single(_stateStore.Pending(A02));
        Assert.Null(queued.RtIdSet);
    }

    [Fact]
    public async Task ADeadJob_DoesNotChargeALateDeliveryThatWasQueuedAfterItStarted()
    {
        // A job whose process died an hour ago; the late delivery arrived after it had started and never ran.
        var started = Start.AddHours(-2);
        await _jobStore.CreateAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, A02, RecomputeJobState.Running, RecomputeTrigger.Periodic,
            LateDay, LateDay.AddDays(1), null, null, null, started, null, null, null, null, started));
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);

        await NewSut().TickAsync(CancellationToken.None);

        var held = Assert.Single(_stateStore.Pending(A02));
        Assert.Equal(0, held.Attempts);
        Assert.True(held.IsDebounced);
    }

    [Fact]
    public async Task ADeadJob_DoesNotChargeARetryThatWasStillHeldBackWhenItStarted()
    {
        // A retry of {M1} with four failed attempts, held back until 12:30; at 12:05 a fresh job of {M1} for one hour
        // started (without the held retry) and its process died.
        var started = Start.AddHours(-2);
        var held = new ArchiveRecomputeRange(A02, LateDay.AddHours(8), LateDay.AddHours(20), null, started.AddHours(-1), 4,
            started.AddMinutes(25), "earlier failure") { RtIdSet = RtIdSet.Of(new[] { M1 }) };
        _stateStore.Pending(A02).Add(held);
        await _jobStore.CreateAsync(new RecomputeJobSnapshot(
            OctoObjectId.Empty, A02, RecomputeJobState.Running, RecomputeTrigger.Periodic,
            LateDay.AddHours(8), LateDay.AddHours(9), null, null, null, started, null, null, null, null, started)
        {
            RtIdSet = RtIdSet.Of(new[] { M1 }),
        });

        await NewSut().TickAsync(CancellationToken.None);

        // The held retry was not part of the dead job: still at attempt 4, not parked (it is due now and runs again).
        Assert.DoesNotContain(_stateStore.Pending(A02), r => r.IsParked);
        Assert.DoesNotContain(_stateStore.Pending(A02), r => r.Attempts >= 5);
    }

    // ---- Failure keeps the scope ----------------------------------------------------------------

    [Fact]
    public async Task AFailedRefresh_HandsTheRemainderBack_WithItsSeries()
    {
        _executor.FailNext = true;
        LateDelivery(LateDay.AddHours(8), LateDay.AddHours(9), M1);

        await NewSut(debounce: TimeSpan.Zero).TickAsync(CancellationToken.None);

        var remainder = Assert.Single(_stateStore.Pending(A02));
        Assert.Equal(RtIdSet.Of(new[] { M1 }), remainder.RtIdSet);
        Assert.Equal(1, remainder.Attempts);
        Assert.False(remainder.IsDebounced);
    }

    // ---- Test doubles ---------------------------------------------------------------------------

    private sealed record ExecutorCall(OctoObjectId Rollup, DateTime From, DateTime To, OctoObjectId? RtIdScope, RtIdSet? RtIds);

    /// <summary>Records every executor call; the in-place refresh and the generation recompute are told apart.</summary>
    private sealed class RecordingExecutor : IArchiveRecomputeExecutor
    {
        public List<ExecutorCall> Calls { get; } = new();

        public bool FailNext { get; set; }

        public Task<RecomputeExecutionResult> ExecuteAsync(
            ArchiveSnapshot source, RollupArchiveSnapshot rollup, DateTime rangeStart, DateTime rangeEnd,
            OctoObjectId? rtIdScope, CancellationToken cancellationToken)
        {
            Calls.Add(new ExecutorCall(rollup.RtId, rangeStart, rangeEnd, rtIdScope, null));
            return Result();
        }

        public Task<RecomputeExecutionResult> ExecuteScopedRefreshAsync(
            ArchiveSnapshot source, RollupArchiveSnapshot rollup, DateTime rangeStart, DateTime rangeEnd,
            RtIdSet rtIds, CancellationToken cancellationToken)
        {
            Calls.Add(new ExecutorCall(rollup.RtId, rangeStart, rangeEnd, null, rtIds));
            return Result();
        }

        private Task<RecomputeExecutionResult> Result()
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("statement failed");
            }

            return Task.FromResult(new RecomputeExecutionResult(10, 1));
        }
    }
}

/// <summary>
/// In-memory <see cref="IArchiveRecomputeStateStore"/> with the value semantics of the MongoDB store: an obligation is
/// removed by handing back the record that was read.
/// </summary>
internal sealed class InMemoryRecomputeStateStore : IArchiveRecomputeStateStore
{
    private readonly Dictionary<OctoObjectId, List<ArchiveDirtyWindow>> _windows = new();
    private readonly Dictionary<OctoObjectId, List<ArchiveRecomputeRange>> _pending = new();

    public List<ArchiveDirtyWindow> Windows(OctoObjectId archive) =>
        _windows.TryGetValue(archive, out var list) ? list : _windows[archive] = new List<ArchiveDirtyWindow>();

    public List<ArchiveRecomputeRange> Pending(OctoObjectId archive) =>
        _pending.TryGetValue(archive, out var list) ? list : _pending[archive] = new List<ArchiveRecomputeRange>();

    public Task AppendDirtyWindowAsync(OctoObjectId archiveRtId, ArchiveDirtyWindow window)
    {
        Windows(archiveRtId).Add(window);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ArchiveDirtyWindow>> GetDirtyWindowsAsync(OctoObjectId archiveRtId) =>
        Task.FromResult<IReadOnlyList<ArchiveDirtyWindow>>(Windows(archiveRtId).ToList());

    public Task ClearDirtyWindowsAsync(OctoObjectId archiveRtId)
    {
        Windows(archiveRtId).Clear();
        return Task.CompletedTask;
    }

    public Task EnqueueRecomputeRangesAsync(OctoObjectId archiveRtId, IReadOnlyList<ArchiveRecomputeRange> ranges)
    {
        Pending(archiveRtId).AddRange(ranges);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ArchiveRecomputeRange>> GetPendingRecomputeRangesAsync(OctoObjectId archiveRtId) =>
        Task.FromResult<IReadOnlyList<ArchiveRecomputeRange>>(Pending(archiveRtId).ToList());

    public Task ClearPendingRecomputeRangesAsync(OctoObjectId archiveRtId)
    {
        Pending(archiveRtId).Clear();
        return Task.CompletedTask;
    }

    public Task UpdatePendingRecomputeRangesAsync(
        OctoObjectId archiveRtId, IReadOnlyList<ArchiveRecomputeRange> remove, IReadOnlyList<ArchiveRecomputeRange> add)
    {
        var toRemove = remove.ToHashSet();
        Pending(archiveRtId).RemoveAll(toRemove.Contains);
        Pending(archiveRtId).AddRange(add);
        return Task.CompletedTask;
    }

    public Task MarkRecomputeStartedAsync(OctoObjectId archiveRtId, DateTime startedAt) => Task.CompletedTask;

    public Task MarkRecomputeSucceededAsync(OctoObjectId archiveRtId, DateTime succeededAt) => Task.CompletedTask;

    public Task MarkRecomputeFailedAsync(OctoObjectId archiveRtId, DateTime failedAt, string reason) => Task.CompletedTask;
}
