using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupOrchestratorTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId RollupRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly RtCkId<CkTypeId> SourceType = new("Test", new CkTypeId("TempSensor"));
    private static readonly DateTime BaseTime = new(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IStreamDataRepository _repo = A.Fake<IStreamDataRepository>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    private RollupOrchestrator NewSut(DateTime now, int maxBucketsPerTick = 60) =>
        new(TenantId, _archiveStore, _rollupStore, _repo, _audit,
            NullLogger<RollupOrchestrator>.Instance, maxBucketsPerTick, () => now);

    private static RollupArchiveSnapshot Rollup(
        DateTime? watermark,
        TimeSpan? bucketSize = null,
        TimeSpan? watermarkLag = null,
        DateTime? frozenUntil = null,
        CkArchiveStatus status = CkArchiveStatus.Activated,
        RollupSourceReference[]? sources = null) =>
        new(
            RollupRt,
            TargetType,
            status,
            null,
            sources ?? new[] { new RollupSourceReference(SourceRt) },
            bucketSize ?? TimeSpan.FromMinutes(1),
            watermarkLag ?? TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            frozenUntil);

    private static ArchiveSnapshot ActivatedSource() =>
        new(SourceRt, SourceType, CkArchiveStatus.Activated, null, Array.Empty<CkArchiveColumnSpec>());

    private void StubActivatedSource() =>
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(ActivatedSource());

    private void StubRollups(params RollupArchiveSnapshot[] rollups) =>
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(rollups));

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    // ---- Bucket loop -----------------------------------------------------------------------

    [Fact]
    public async Task Tick_NoRollups_ReturnsZero()
    {
        StubRollups();

        var n = await NewSut(BaseTime).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_ActivatedRollup_NotEnoughLagYet_NoOps()
    {
        // BucketSize=1m, Lag=5m, watermark at BaseTime. now = watermark + 1m + lag - 1s (just under cutoff).
        var watermark = BaseTime;
        var now = BaseTime + TimeSpan.FromMinutes(1) + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1);
        StubRollups(Rollup(watermark));
        StubActivatedSource();

        var n = await NewSut(now).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(A<OctoObjectId>._, A<DateTime>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_ActivatedRollup_ClosedBuckets_AggregatesAndAdvances()
    {
        // BucketSize=1m, Lag=5m, watermark=BaseTime, now=BaseTime+8m → 3 closed buckets:
        // [t+0,t+1) [t+1,t+2) [t+2,t+3); cutoff = now - 5m = t+3m, so bucketEnd<=t+3m qualifies.
        var watermark = BaseTime;
        var now = BaseTime + TimeSpan.FromMinutes(8);
        var bucketSize = TimeSpan.FromMinutes(1);
        StubRollups(Rollup(watermark, bucketSize));
        StubActivatedSource();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .Returns(5);

        var n = await NewSut(now).TickAsync(CancellationToken.None);

        Assert.Equal(3, n);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt),
                A<RollupArchiveSnapshot>.That.Matches(r => r.RtId == RollupRt),
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, BaseTime + bucketSize, false))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, BaseTime + bucketSize * 2, false))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, BaseTime + bucketSize * 3, false))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordRollupRunAsync(
                TenantId, RollupRt, A<DateTime>._, A<DateTime>._, 5, A<TimeSpan>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task Tick_RespectsMaxBucketsPerTick()
    {
        // 10 closed buckets available; max = 4 → only 4 are processed.
        var watermark = BaseTime;
        var bucketSize = TimeSpan.FromMinutes(1);
        var now = BaseTime + TimeSpan.FromMinutes(15); // plenty of room
        StubRollups(Rollup(watermark, bucketSize));
        StubActivatedSource();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .Returns(1);

        var n = await NewSut(now, maxBucketsPerTick: 4).TickAsync(CancellationToken.None);

        Assert.Equal(4, n);
    }

    [Fact]
    public async Task Tick_FrozenRange_SkipsBucketsButAdvancesWatermark()
    {
        // BucketSize=1m, frozen until t+2m, watermark=t+0, now=t+10m: buckets ending ≤ t+2m
        // are inside the frozen range and skipped (no AggregateBucket calls), but watermark
        // still advances. Bucket [t+2,t+3) — bucketEnd = t+3 > frozenUntil → aggregated.
        var watermark = BaseTime;
        var bucketSize = TimeSpan.FromMinutes(1);
        var frozenUntil = BaseTime + TimeSpan.FromMinutes(2);
        var now = BaseTime + TimeSpan.FromMinutes(10);
        StubRollups(Rollup(watermark, bucketSize, frozenUntil: frozenUntil));
        StubActivatedSource();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .Returns(1);

        var n = await NewSut(now, maxBucketsPerTick: 3).TickAsync(CancellationToken.None);

        // One real aggregation (bucket [t+2,t+3))
        Assert.Equal(1, n);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                BaseTime + bucketSize * 2, BaseTime + bucketSize * 3,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // Frozen-range buckets must not aggregate.
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>.That.Matches(d => d <= frozenUntil),
                A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ---- Gates -----------------------------------------------------------------------------

    [Fact]
    public async Task Tick_NonActivatedRollup_Skipped()
    {
        StubRollups(Rollup(BaseTime, status: CkArchiveStatus.Disabled));
        StubActivatedSource();

        var n = await NewSut(BaseTime + TimeSpan.FromHours(1)).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_NullWatermark_Skipped()
    {
        StubRollups(Rollup(watermark: null));
        StubActivatedSource();

        var n = await NewSut(BaseTime + TimeSpan.FromHours(1)).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Tick_SourceMissing_Skipped()
    {
        StubRollups(Rollup(BaseTime));
        A.CallTo(() => _archiveStore.GetAsync(SourceRt))
            .Returns(Task.FromResult<ArchiveSnapshot?>(null));

        var n = await NewSut(BaseTime + TimeSpan.FromHours(1)).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Tick_SourceNotActivated_Skipped()
    {
        StubRollups(Rollup(BaseTime));
        A.CallTo(() => _archiveStore.GetAsync(SourceRt))
            .Returns(new ArchiveSnapshot(SourceRt, SourceType, CkArchiveStatus.Disabled, null, Array.Empty<CkArchiveColumnSpec>()));

        var n = await NewSut(BaseTime + TimeSpan.FromHours(1)).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
    }

    [Fact]
    public async Task Tick_RollupFailure_DoesNotAbortOthers()
    {
        var otherRt = OctoObjectId.GenerateNewId();
        var first = Rollup(BaseTime);
        var second = new RollupArchiveSnapshot(
            otherRt, TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(SourceRt) },
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
            BaseTime, // watermark
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);
        StubRollups(first, second);
        StubActivatedSource();

        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._,
                A<RollupArchiveSnapshot>.That.Matches(r => r.RtId == RollupRt),
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .Throws<InvalidOperationException>();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._,
                A<RollupArchiveSnapshot>.That.Matches(r => r.RtId == otherRt),
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .Returns(1);

        var n = await NewSut(BaseTime + TimeSpan.FromMinutes(8), maxBucketsPerTick: 1)
            .TickAsync(CancellationToken.None);

        // First rollup threw on its first bucket; second rollup processed 1 bucket.
        Assert.Equal(1, n);
    }

    // ---- ProcessRollupAsync + Rewind ---------------------------------------------------------

    [Fact]
    public async Task ProcessRollup_UnknownRollup_ThrowsArchiveNotFoundException()
    {
        A.CallTo(() => _rollupStore.GetAsync(RollupRt))
            .Returns(Task.FromResult<RollupArchiveSnapshot?>(null));

        await Assert.ThrowsAsync<ArchiveNotFoundException>(
            () => NewSut(BaseTime).ProcessRollupAsync(RollupRt, CancellationToken.None));
    }

    [Fact]
    public async Task Rewind_TruncatesAndAllowsBackwardWrite()
    {
        // BucketSize=1m; passing t+0:42 → truncates to t+0:00.
        var passedIn = BaseTime + TimeSpan.FromSeconds(42);
        var expected = BaseTime;
        A.CallTo(() => _rollupStore.GetAsync(RollupRt))
            .Returns(Rollup(BaseTime + TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1)));

        await NewSut(BaseTime).RewindWatermarkAsync(RollupRt, passedIn);

        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, expected, true))
            .MustHaveHappenedOnceExactly();
    }

    // ---- Open-bucket refresh (AB#4306) -----------------------------------------------------

    private RollupOrchestrator NewSutWithRefresh(DateTime now, int maxBucketsPerTick = 60) =>
        new(TenantId, _archiveStore, _rollupStore, _repo, _audit,
            NullLogger<RollupOrchestrator>.Instance, maxBucketsPerTick, () => now, refreshOpenBucket: true);

    [Fact]
    public async Task Tick_RefreshOpenBucket_UpsertsCurrentOpenBucketWithoutAdvancingWatermark()
    {
        // watermark at 14:00, now 14:00:30 → the [14:00,14:01) bucket is still open (end > now-lag),
        // so the closed loop commits nothing. With refresh on, that one open bucket is re-aggregated.
        var bucketSize = TimeSpan.FromMinutes(1);
        StubActivatedSource();
        StubRollups(Rollup(BaseTime, bucketSize, TimeSpan.FromMinutes(5)));

        var committed = await NewSutWithRefresh(BaseTime + TimeSpan.FromSeconds(30)).TickAsync(CancellationToken.None);

        Assert.Equal(0, committed); // no CLOSED bucket committed
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, BaseTime, BaseTime + bucketSize, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // The open bucket must NOT advance the watermark — it stays open for the next tick.
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(A<OctoObjectId>._, A<DateTime>._, A<bool>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_RefreshOpenBucketDisabled_DoesNotUpsertOpenBucket()
    {
        StubActivatedSource();
        StubRollups(Rollup(BaseTime, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)));

        await NewSut(BaseTime + TimeSpan.FromSeconds(30)).TickAsync(CancellationToken.None);

        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_RefreshOpenBucket_SkippedWhileClosedBacklogStillDraining()
    {
        // watermark far behind + a per-tick cap of 2 → the loop commits 2 CLOSED buckets and stops on
        // the cap, not at the current open bucket. The refresh must then NOT fire (no 3rd aggregate):
        // openEnd is still ≤ now-lag, so the closed loop owns it on the next tick.
        var bucketSize = TimeSpan.FromMinutes(1);
        StubActivatedSource();
        StubRollups(Rollup(BaseTime - TimeSpan.FromMinutes(10), bucketSize, TimeSpan.FromMinutes(5)));

        await NewSutWithRefresh(BaseTime, maxBucketsPerTick: 2).TickAsync(CancellationToken.None);

        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._, A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly); // exactly the 2 closed buckets, no open-bucket upsert
    }

    // ---- Derived-columns self-heal (AB#4772) -----------------------------------------------

    [Fact]
    public async Task Tick_RollupWithoutPersistedColumns_HealsEvenWhenNotActivated()
    {
        // Disabled rollups never reach the aggregation path, but the heal must still run —
        // the broken Columns attribute breaks the archives GraphQL regardless of status.
        StubRollups(Rollup(BaseTime, status: CkArchiveStatus.Disabled) with { HasPersistedColumns = false });

        await NewSut(BaseTime).TickAsync(CancellationToken.None);

        A.CallTo(() => _rollupStore.TryPersistDerivedColumnsAsync(RollupRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Tick_RollupWithPersistedColumns_DoesNotCallHeal()
    {
        StubActivatedSource();
        StubRollups(Rollup(BaseTime)); // HasPersistedColumns defaults to true

        await NewSut(BaseTime).TickAsync(CancellationToken.None);

        A.CallTo(() => _rollupStore.TryPersistDerivedColumnsAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_HealFailure_DoesNotStopTheTick()
    {
        StubActivatedSource();
        StubRollups(Rollup(BaseTime) with { HasPersistedColumns = false });
        A.CallTo(() => _rollupStore.TryPersistDerivedColumnsAsync(RollupRt))
            .Throws(new InvalidOperationException("boom"));

        // Must not surface the heal failure; the rollup itself still gets processed (no lag due
        // here, so 0 buckets — the assertion is simply that TickAsync completes).
        var n = await NewSut(BaseTime).TickAsync(CancellationToken.None);

        Assert.Equal(0, n);
    }
    // ---- AB#5157: per-bucket source selection -----------------------------------------------

    private static readonly OctoObjectId NativeRt = OctoObjectId.GenerateNewId();

    /// The cutover instant of the legacy/native pair; BaseTime is on the hourly bucket grid.
    private static readonly DateTime Cutover = BaseTime;

    private static RollupSourceReference[] CutoverSources() => new[]
    {
        new RollupSourceReference(SourceRt, ValidTo: Cutover),
        new RollupSourceReference(NativeRt, ValidFrom: Cutover),
    };

    private void StubNativeSource(CkArchiveStatus status = CkArchiveStatus.Activated) =>
        A.CallTo(() => _archiveStore.GetAsync(NativeRt))
            .Returns(new ArchiveSnapshot(NativeRt, SourceType, status, null, Array.Empty<CkArchiveColumnSpec>()));

    // TC-MODEL-04: ValidFrom is inclusive and ValidTo exclusive, so the bucket that STARTS at the
    // cutover is served by the native source and the one that ENDS there by the legacy source.
    [Fact]
    public async Task Tick_TwoSourcesAcrossTheCutover_UsesLegacyBeforeAndNativeFromTheCutoverBucket()
    {
        var bucketSize = TimeSpan.FromHours(1);
        var watermark = Cutover - TimeSpan.FromHours(2);                 // 12:00
        var now = Cutover + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(10); // 16:10, lag 5 min ⇒ cutoff 16:05
        StubRollups(Rollup(watermark, bucketSize, sources: CutoverSources()));
        StubActivatedSource();
        StubNativeSource();

        var committed = await NewSut(now).TickAsync(CancellationToken.None);

        Assert.Equal(4, committed);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt), A<RollupArchiveSnapshot>._,
                Cutover - TimeSpan.FromHours(2), Cutover - TimeSpan.FromHours(1), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt), A<RollupArchiveSnapshot>._,
                Cutover - TimeSpan.FromHours(1), Cutover, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // The cutover bucket itself belongs to the native source (half-open span).
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == NativeRt), A<RollupArchiveSnapshot>._,
                Cutover, Cutover + TimeSpan.FromHours(1), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == NativeRt), A<RollupArchiveSnapshot>._,
                Cutover + TimeSpan.FromHours(1), Cutover + TimeSpan.FromHours(2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // No bucket from the cutover onwards may be aggregated from the legacy source.
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt), A<RollupArchiveSnapshot>._,
                A<DateTime>.That.Matches(d => d >= Cutover), A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_TwoSources_LoadsEachSourceSnapshotOncePerTick()
    {
        var bucketSize = TimeSpan.FromHours(1);
        var watermark = Cutover - TimeSpan.FromHours(2);
        var now = Cutover + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(10);
        StubRollups(Rollup(watermark, bucketSize, sources: CutoverSources()));
        StubActivatedSource();
        StubNativeSource();

        await NewSut(now).TickAsync(CancellationToken.None);

        // Four buckets, two sources — but one store lookup per distinct source for the whole tick.
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _archiveStore.GetAsync(NativeRt)).MustHaveHappenedOnceExactly();
    }

    // TC-AGG-03: a bucket no span covers writes no row, but the watermark moves on so the rollup
    // does not stall on a deliberately uncovered range.
    [Fact]
    public async Task Tick_BucketCoveredByNoSpan_AdvancesTheWatermarkWithoutAggregating()
    {
        var bucketSize = TimeSpan.FromHours(1);
        var gapStart = Cutover;                             // 14:00 — legacy ends here
        var gapEnd = Cutover + TimeSpan.FromHours(2);       // 16:00 — native starts here
        var watermark = Cutover - TimeSpan.FromHours(1);    // 13:00
        var now = Cutover + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10); // 17:10 ⇒ cutoff 17:05
        StubRollups(Rollup(watermark, bucketSize, sources: new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: gapStart),
            new RollupSourceReference(NativeRt, ValidFrom: gapEnd),
        }));
        StubActivatedSource();
        StubNativeSource();

        var committed = await NewSut(now).TickAsync(CancellationToken.None);

        // Only the legacy bucket [13,14) and the native bucket [16,17) are committed; the two gap
        // buckets are not counted.
        Assert.Equal(2, committed);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>.That.Matches(d => d >= gapStart && d < gapEnd), A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        // …yet the watermark walked across the gap.
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, gapStart + TimeSpan.FromHours(1), false))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, gapEnd, false))
            .MustHaveHappenedOnceExactly();
    }

    // The bucket's source is not activated (a disabled source stays allowed): the tick stops there
    // with the watermark unchanged instead of skipping past data the source will deliver later.
    [Fact]
    public async Task Tick_SourceOfALaterBucketNotActivated_StopsTickWithThatWatermarkUnchanged()
    {
        var bucketSize = TimeSpan.FromHours(1);
        var watermark = Cutover - TimeSpan.FromHours(2);                       // 12:00
        var now = Cutover + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10);  // 17:10
        StubRollups(Rollup(watermark, bucketSize, sources: CutoverSources()));
        StubActivatedSource();
        StubNativeSource(CkArchiveStatus.Disabled);

        var committed = await NewSut(now).TickAsync(CancellationToken.None);

        // The two legacy buckets stay committed; the cutover bucket stalls.
        Assert.Equal(2, committed);
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, Cutover, false))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _rollupStore.AdvanceWatermarkAsync(RollupRt, Cutover + TimeSpan.FromHours(1), false))
            .MustNotHaveHappened();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == NativeRt), A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // TC-AGG-12: the open bucket resolves its source per bucket exactly like a closed one.
    [Fact]
    public async Task Tick_RefreshOpenBucket_UsesTheSourceCoveringTheOpenBucket()
    {
        var bucketSize = TimeSpan.FromHours(1);
        // Watermark exactly at the cutover ⇒ the open bucket [14:00, 15:00) lies in the native span.
        StubRollups(Rollup(Cutover, bucketSize, sources: CutoverSources()));
        StubActivatedSource();
        StubNativeSource();

        var committed = await NewSutWithRefresh(Cutover + TimeSpan.FromMinutes(30)).TickAsync(CancellationToken.None);

        Assert.Equal(0, committed);
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == NativeRt), A<RollupArchiveSnapshot>._,
                Cutover, Cutover + bucketSize, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>.That.Matches(s => s.RtId == SourceRt), A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Tick_RefreshOpenBucket_OpenBucketCoveredByNoSpan_IsSkipped()
    {
        var bucketSize = TimeSpan.FromHours(1);
        // The only source ends at the cutover, so the open bucket [14:00, 15:00) is uncovered.
        StubRollups(Rollup(Cutover, bucketSize, sources: new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Cutover),
        }));
        StubActivatedSource();

        await NewSutWithRefresh(Cutover + TimeSpan.FromMinutes(30)).TickAsync(CancellationToken.None);

        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>._, A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // TC-X-AGG-03: the initial watermark of a freshly activated rollup is one documented value
    // derived from the clock alone — a second source does not move it, and no historic bucket is
    // produced before an explicit backfill.
    [Fact]
    public async Task Tick_NewlyActivatedTwoSourceRollup_StartsAtTheSameClockDerivedInitialWatermark()
    {
        var bucketSize = TimeSpan.FromHours(1);
        var activationTime = Cutover + TimeSpan.FromMinutes(30);
        // The seed takes the clock and the rollup's own bucket geometry — the very arguments a
        // single-source rollup passes — and never looks at a source archive.
        var initialWatermark = BucketBoundary.InitialWatermark(
            activationTime, BucketAlignment.FixedSize, bucketSize);
        Assert.Equal(Cutover - TimeSpan.FromHours(1), initialWatermark);

        var now = activationTime + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(10);
        StubRollups(Rollup(initialWatermark, bucketSize, sources: CutoverSources()));
        StubActivatedSource();
        StubNativeSource();

        await NewSut(now).TickAsync(CancellationToken.None);

        // Aggregation starts AT the initial watermark; nothing older is produced.
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                A<DateTime>.That.Matches(d => d < initialWatermark), A<DateTime>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _repo.AggregateBucketAsync(
                A<ArchiveSnapshot>._, A<RollupArchiveSnapshot>._,
                initialWatermark, initialWatermark + bucketSize, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
