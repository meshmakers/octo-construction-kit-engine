using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupArchiveLifecycleServiceTests
{
    private const string TenantId = "tenant-x";
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));

    private readonly IRollupArchiveRuntimeStore _store = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IArchiveAuditTrail _audit = A.Fake<IArchiveAuditTrail>();

    private RollupArchiveLifecycleService NewSut(IStreamDataRepository? streamData = null) =>
        new(TenantId, _store, _archiveStore, _audit, NullLogger<RollupArchiveLifecycleService>.Instance, streamData);

    private static RollupArchiveSnapshot Snapshot(
        DateTime? frozenUntil = null,
        DateTime? watermark = null,
        TimeSpan? bucketSize = null,
        BucketAlignment alignment = BucketAlignment.FixedSize) =>
        new(
            Rt,
            TargetType,
            CkArchiveStatus.Activated,
            null,
            SourceRt,
            bucketSize ?? TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            frozenUntil)
        {
            BucketAlignment = alignment
        };

    // ---- Create ----

    [Fact]
    public async Task Create_ResolvesTargetCkTypeFromSourceAndDerivesColumns()
    {
        // Source archive carries its TargetCkTypeId — the rollup inherits it. Columns are derived
        // server-side from the aggregations; AVG produces two columns (sum + count).
        var sourceSnapshot = new ArchiveSnapshot(
            SourceRt,
            TargetType,
            CkArchiveStatus.Activated,
            "SourceArchive",
            new[] { new CkArchiveColumnSpec("voltage", Indexed: true, Required: false) });
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(sourceSnapshot);

        var insertedRtId = OctoObjectId.GenerateNewId();
        A.CallTo(() => _store.InsertAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<OctoObjectId>._, A<TimeSpan>._, A<TimeSpan>._,
                A<IReadOnlyList<CkRollupAggregationSpec>>._, A<IReadOnlyList<CkArchiveColumnSpec>>._))
            .Returns(insertedRtId);

        var aggregations = new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) };

        var rtId = await NewSut().CreateAsync(
            "MyRollup", SourceRt, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), aggregations);

        Assert.Equal(insertedRtId, rtId);
        A.CallTo(() => _store.InsertAsync(
                "MyRollup", TargetType, SourceRt,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
                aggregations,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(cols =>
                    cols.Count == 2 &&
                    cols[0].Path == "voltage_avg_sum" &&
                    cols[1].Path == "voltage_avg_count")))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Create_UnknownSourceArchive_ThrowsArchiveNotFound()
    {
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Task.FromResult<ArchiveSnapshot?>(null));

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => NewSut().CreateAsync(
            null, SourceRt, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }));

        A.CallTo(() => _store.InsertAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<OctoObjectId>._, A<TimeSpan>._, A<TimeSpan>._,
                A<IReadOnlyList<CkRollupAggregationSpec>>._, A<IReadOnlyList<CkArchiveColumnSpec>>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Create_EmptyAggregations_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => NewSut().CreateAsync(
            null, SourceRt, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            Array.Empty<CkRollupAggregationSpec>()));
    }

    [Fact]
    public async Task Create_NonPositiveBucketSize_ThrowsArgumentOutOfRange()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NewSut().CreateAsync(
            null, SourceRt, TimeSpan.Zero, TimeSpan.FromMinutes(1),
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }));
    }

    // ---- Freeze ----

    [Fact]
    public async Task Freeze_FromUnfrozen_PersistsAndAudits()
    {
        var until = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot());

        await NewSut().FreezeAsync(Rt, until);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, until)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordFreezeAsync(TenantId, Rt, until, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Freeze_MovingForward_PersistsAndAudits()
    {
        var current = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc);
        var newer = current.AddHours(4);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await NewSut().FreezeAsync(Rt, newer);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, newer)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _audit.RecordFreezeAsync(TenantId, Rt, newer, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Freeze_Backwards_ThrowsAndDoesNotWrite()
    {
        var current = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        var earlier = current.AddHours(-2);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await Assert.ThrowsAsync<InvalidArchiveStateTransitionException>(
            () => NewSut().FreezeAsync(Rt, earlier));

        A.CallTo(() => _store.SetFrozenUntilAsync(A<OctoObjectId>._, A<DateTime?>._)).MustNotHaveHappened();
        A.CallTo(() => _audit.RecordFreezeAsync(A<string>._, A<OctoObjectId>._, A<DateTime>._, A<string?>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Freeze_UnknownRollup_ThrowsArchiveNotFoundException()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Task.FromResult<RollupArchiveSnapshot?>(null));

        await Assert.ThrowsAsync<ArchiveNotFoundException>(
            () => NewSut().FreezeAsync(Rt, DateTime.UtcNow));
    }

    // ---- Unfreeze ----

    [Fact]
    public async Task Unfreeze_WhenFrozen_ClearsFrozenUntil()
    {
        var current = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: current));

        await NewSut().UnfreezeAsync(Rt);

        A.CallTo(() => _store.SetFrozenUntilAsync(Rt, null)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Unfreeze_NotFrozen_NoOps()
    {
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(frozenUntil: null));

        await NewSut().UnfreezeAsync(Rt);

        A.CallTo(() => _store.SetFrozenUntilAsync(A<OctoObjectId>._, A<DateTime?>._)).MustNotHaveHappened();
    }

    // ---- Rewind watermark ----

    [Fact]
    public async Task Rewind_TruncatesToBucketBoundary_AndAllowsRewind()
    {
        // BucketSize = 1 min; passing 14:00:42 → should truncate to 14:00:00.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        var expectedBucketEnd = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.FromMinutes(1)));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, expectedBucketEnd, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Rewind_CalendarDayAlignment_SnapsToStartOfDay()
    {
        // Regression: a CalendarDay rollup must snap the rewind target to the start of its calendar
        // day (00:00:00), not preserve the wall-clock time-of-day. The previous FixedSize-only modulo
        // arithmetic ignored BucketAlignment and left the watermark — and every re-aggregated bucket —
        // offset by the passed-in seconds. BucketSize is informational for calendar variants.
        var passedIn = new DateTime(2026, 6, 24, 0, 0, 48, 554, DateTimeKind.Utc);
        var expectedBucketEnd = new DateTime(2026, 6, 24, 0, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(
            Snapshot(bucketSize: TimeSpan.FromDays(1), alignment: BucketAlignment.CalendarDay));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, expectedBucketEnd, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Rewind_ZeroBucketSize_PassesTimestampUnchanged()
    {
        // Defensive: a bucket-size of 0 should not divide-by-zero; pass the value through.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.Zero));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, passedIn, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Rewind_WithStreamData_ClearsRecomputeGenerationsAtBucketBoundary()
    {
        // AB#4184, Phase 6: a rewind over a previously-recomputed range must clear the active
        // generation pointers so the forward re-aggregation (generation 0) becomes authoritative.
        // The clear is issued with the same truncated bucket boundary as the watermark advance.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        var expectedBucketEnd = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        var streamData = A.Fake<IStreamDataRepository>();
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.FromMinutes(1)));

        await NewSut(streamData).RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, expectedBucketEnd, true))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => streamData.ClearRecomputeGenerationsAsync(
                Rt, expectedBucketEnd, A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Rewind_WithoutStreamData_DoesNotThrow()
    {
        // Stream data disabled (null repository) ⇒ the rewind still advances the watermark and
        // simply skips the generation-pointer reconciliation.
        var passedIn = new DateTime(2026, 5, 11, 14, 0, 42, DateTimeKind.Utc);
        var expectedBucketEnd = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
        A.CallTo(() => _store.GetAsync(Rt)).Returns(Snapshot(bucketSize: TimeSpan.FromMinutes(1)));

        await NewSut().RewindWatermarkAsync(Rt, passedIn);

        A.CallTo(() => _store.AdvanceWatermarkAsync(Rt, expectedBucketEnd, true))
            .MustHaveHappenedOnceExactly();
    }
}
