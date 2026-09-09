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
    private static readonly OctoObjectId SecondSourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly RtCkId<CkTypeId> OtherTargetType = new("Test", new CkTypeId("OtherType"));

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
            new[] { new RollupSourceReference(SourceRt) },
            bucketSize ?? TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            watermark,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            frozenUntil)
        {
            BucketAlignment = alignment
        };

    private static readonly CkRollupAggregationSpec[] VoltageAvg =
        { new("voltage", CkRollupFunction.Avg, null) };

    private static RollupSourceReference[] Sources(params OctoObjectId[] rtIds) =>
        Array.ConvertAll(rtIds, id => new RollupSourceReference(id));

    private static ArchiveSnapshot SourceArchive(OctoObjectId rtId, RtCkId<CkTypeId>? targetType = null) =>
        new(
            rtId,
            targetType ?? TargetType,
            CkArchiveStatus.Activated,
            "SourceArchive",
            new[] { new CkArchiveColumnSpec("voltage", Indexed: true, Required: false) });

    private void StubSource(OctoObjectId rtId, RtCkId<CkTypeId>? targetType = null) =>
        A.CallTo(() => _archiveStore.GetAsync(rtId)).Returns(SourceArchive(rtId, targetType));

    private void StubInsert(OctoObjectId insertedRtId) =>
        A.CallTo(() => _store.InsertAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<IReadOnlyList<RollupSourceReference>>._,
                A<TimeSpan>._, A<TimeSpan>._,
                A<IReadOnlyList<CkRollupAggregationSpec>>._, A<IReadOnlyList<CkArchiveColumnSpec>>._,
                A<BucketAlignment>._, A<string?>._, A<TimeSpan?>._))
            .Returns(insertedRtId);

    private void MustNotHaveInserted() =>
        A.CallTo(() => _store.InsertAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<IReadOnlyList<RollupSourceReference>>._,
                A<TimeSpan>._, A<TimeSpan>._,
                A<IReadOnlyList<CkRollupAggregationSpec>>._, A<IReadOnlyList<CkArchiveColumnSpec>>._,
                A<BucketAlignment>._, A<string?>._, A<TimeSpan?>._))
            .MustNotHaveHappened();

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    // ---- Create ----

    [Fact]
    public async Task Create_ResolvesTargetCkTypeFromSourceAndDerivesColumns()
    {
        // Source archive carries its TargetCkTypeId — the rollup inherits it. Columns are derived
        // server-side from the aggregations; AVG produces two columns (sum + count).
        StubSource(SourceRt);
        var insertedRtId = OctoObjectId.GenerateNewId();
        StubInsert(insertedRtId);

        var sources = Sources(SourceRt);
        var rtId = await NewSut().CreateAsync(
            "MyRollup", sources, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), VoltageAvg);

        Assert.Equal(insertedRtId, rtId);
        A.CallTo(() => _store.InsertAsync(
                "MyRollup", TargetType, sources,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
                VoltageAvg,
                A<IReadOnlyList<CkArchiveColumnSpec>>.That.Matches(cols =>
                    cols.Count == 2 &&
                    cols[0].Path == "voltage_avg_sum" &&
                    cols[1].Path == "voltage_avg_count"),
                BucketAlignment.FixedSize, null, null))
            .MustHaveHappenedOnceExactly();
    }

    // AB#5157: two disjoint sources reach the store as one list, in declaration order.
    [Fact]
    public async Task Create_TwoDisjointSources_PassesThemToInsertInOrder()
    {
        StubSource(SourceRt);
        StubSource(SecondSourceRt);
        StubInsert(OctoObjectId.GenerateNewId());

        var cutover = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sources = new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        };

        await NewSut().CreateAsync(
            "Cutover", sources, TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg,
            BucketAlignment.CalendarDay);

        A.CallTo(() => _store.InsertAsync(
                "Cutover", TargetType,
                A<IReadOnlyList<RollupSourceReference>>.That.Matches(s =>
                    s.Count == 2 &&
                    s[0].SourceArchiveRtId == SourceRt && s[0].ValidTo == cutover &&
                    s[1].SourceArchiveRtId == SecondSourceRt && s[1].ValidFrom == cutover),
                A<TimeSpan>._, A<TimeSpan>._, A<IReadOnlyList<CkRollupAggregationSpec>>._,
                A<IReadOnlyList<CkArchiveColumnSpec>>._, BucketAlignment.CalendarDay, null, null))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _archiveStore.GetAsync(SecondSourceRt)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Create_UnknownSourceArchive_ThrowsArchiveNotFound()
    {
        A.CallTo(() => _archiveStore.GetAsync(SourceRt)).Returns(Task.FromResult<ArchiveSnapshot?>(null));

        var ex = await Assert.ThrowsAsync<ArchiveNotFoundException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), VoltageAvg));

        Assert.Contains(SourceRt.ToString(), ex.Message);
        MustNotHaveInserted();
    }

    [Fact]
    public async Task Create_SecondSourceUnknown_ThrowsArchiveNotFoundNamingIt()
    {
        StubSource(SourceRt);
        A.CallTo(() => _archiveStore.GetAsync(SecondSourceRt)).Returns(Task.FromResult<ArchiveSnapshot?>(null));

        var cutover = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<ArchiveNotFoundException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: cutover),
                new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
        MustNotHaveInserted();
    }

    // TC-VAL-11 at create time: the second source targets another CK type.
    [Fact]
    public async Task Create_SecondSourceOtherTargetType_ThrowsTargetTypeMismatch()
    {
        StubSource(SourceRt);
        StubSource(SecondSourceRt, OtherTargetType);

        var cutover = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<RollupSourceTargetTypeMismatchException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: cutover),
                new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Equal(TargetType, ex.ExpectedTargetCkTypeId);
        Assert.Equal(OtherTargetType, ex.ActualTargetCkTypeId);
        MustNotHaveInserted();
    }

    // TC-VAL-19: an empty sources list never reaches the store.
    [Fact]
    public async Task Create_EmptySources_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => NewSut().CreateAsync(
            null, Array.Empty<RollupSourceReference>(), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            VoltageAvg));

        MustNotHaveInserted();
    }

    [Fact]
    public async Task Create_NullSources_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => NewSut().CreateAsync(
            null, null!, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), VoltageAvg));
    }

    // TC-VAL-01: overlapping spans are rejected at create, before the insert.
    [Fact]
    public async Task Create_OverlappingSpans_ThrowsOverlapAndDoesNotInsert()
    {
        StubSource(SourceRt);
        StubSource(SecondSourceRt);

        var ex = await Assert.ThrowsAsync<RollupSourceSpanOverlapException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
                new RollupSourceReference(SecondSourceRt, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        MustNotHaveInserted();
    }

    // TC-VAL-20: the same archive twice is rejected at create.
    [Fact]
    public async Task Create_DuplicateSourceArchive_ThrowsAndDoesNotInsert()
    {
        StubSource(SourceRt);

        var cutover = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<DuplicateRollupSourceException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: cutover),
                new RollupSourceReference(SourceRt, ValidFrom: cutover),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        MustNotHaveInserted();
    }

    // A rule that fires during Create has no rollup id to name — the message must say so instead of
    // printing the all-zero placeholder, which reads as a real archive nobody can find (AB#5157
    // review). Covers every rollup lifecycle message: they all render the id the same way.
    [Fact]
    public async Task Create_ValidationMessage_NamesNoPlaceholderId()
    {
        StubSource(SourceRt);

        var cutover = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<DuplicateRollupSourceException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: cutover),
                new RollupSourceReference(SourceRt, ValidFrom: cutover),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.DoesNotContain(OctoObjectId.Empty.ToString(), ex.Message);
        Assert.Contains("(not yet created)", ex.Message);
        // The offending source is still named — that is what the operator can act on.
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    // TC-X-VAL-02: an inverted span is rejected at create.
    [Fact]
    public async Task Create_InvertedSpan_ThrowsAndDoesNotInsert()
    {
        StubSource(SourceRt);

        await Assert.ThrowsAsync<RollupSourceSpanInvertedException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt,
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        MustNotHaveInserted();
    }

    // TC-VAL-04: two open starts are rejected at create.
    [Fact]
    public async Task Create_TwoOpenStarts_ThrowsAndDoesNotInsert()
    {
        StubSource(SourceRt);
        StubSource(SecondSourceRt);

        var ex = await Assert.ThrowsAsync<RollupSourceSpanOpenEndConflictException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt, ValidTo: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new RollupSourceReference(SecondSourceRt, ValidTo: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(1), VoltageAvg, BucketAlignment.CalendarDay));

        Assert.True(ex.IsOpenStart);
        MustNotHaveInserted();
    }

    // TC-VAL-07: a boundary off the rollup's bucket grid is rejected at create.
    [Fact]
    public async Task Create_BoundaryOffTheBucketGrid_ThrowsAndDoesNotInsert()
    {
        StubSource(SourceRt);

        var ex = await Assert.ThrowsAsync<RollupSourceSpanNotOnBucketBoundaryException>(() => NewSut().CreateAsync(
            null,
            new[]
            {
                new RollupSourceReference(SourceRt,
                    ValidTo: new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Utc)),
            },
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), VoltageAvg));

        Assert.Equal(nameof(RollupSourceReference.ValidTo), ex.BoundaryName);
        MustNotHaveInserted();
    }

    // TC-VAL-18: a transitive cycle through an existing rollup is rejected at create.
    [Fact]
    public async Task Create_TransitiveCycleThroughAnExistingRollup_ThrowsAndDoesNotInsert()
    {
        // The prospective rollup is created with rtId Empty, so the existing rollup must point at
        // OctoObjectId.Empty for the walk to close the cycle — which is exactly what an existing
        // rollup referencing "the rollup being created" looks like from CreateAsync's viewpoint.
        StubSource(SourceRt);
        var existing = new RollupArchiveSnapshot(
            SourceRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(OctoObjectId.Empty) },
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), null, VoltageAvg, null);
        A.CallTo(() => _store.EnumerateAsync()).Returns(ToAsync(new[] { existing }));

        var ex = await Assert.ThrowsAsync<RollupSourceCycleException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), VoltageAvg));

        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        MustNotHaveInserted();
    }

    // AB#5157 behaviour change: the aggregation rules now run at create as well.
    [Fact]
    public async Task Create_DuplicateAggregation_ThrowsAtCreate()
    {
        StubSource(SourceRt);

        await Assert.ThrowsAsync<DuplicateRollupAggregationException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            new[]
            {
                new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null),
                new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, "other_name"),
            }));

        MustNotHaveInserted();
    }

    [Fact]
    public async Task Create_StateDurationWithoutComparisonValue_ThrowsAtCreate()
    {
        StubSource(SourceRt);

        await Assert.ThrowsAsync<RollupComparisonValueRequiredException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            new[] { new CkRollupAggregationSpec("isOn", CkRollupFunction.StateDuration, null) }));

        MustNotHaveInserted();
    }

    [Fact]
    public async Task Create_EmptyAggregations_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1),
            Array.Empty<CkRollupAggregationSpec>()));
    }

    [Fact]
    public async Task Create_NonPositiveBucketSize_ThrowsArgumentOutOfRange()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NewSut().CreateAsync(
            null, Sources(SourceRt), TimeSpan.Zero, TimeSpan.FromMinutes(1), VoltageAvg));
    }

    [Fact]
    public async Task Create_PassesAlignmentAndReferenceTimeZoneToStore()
    {
        // AB#4300 — a calendar-aligned rollup with an IANA reference zone must reach the store so the
        // orchestrator can produce DST-correct local-day buckets.
        StubSource(SourceRt);
        StubInsert(OctoObjectId.GenerateNewId());

        var sources = Sources(SourceRt);
        await NewSut().CreateAsync(
            "DailyLocal", sources, TimeSpan.FromDays(1), TimeSpan.FromMinutes(15), VoltageAvg,
            BucketAlignment.CalendarDay, "Europe/Vienna");

        A.CallTo(() => _store.InsertAsync(
                "DailyLocal", TargetType, sources,
                TimeSpan.FromDays(1), TimeSpan.FromMinutes(15),
                VoltageAvg, A<IReadOnlyList<CkArchiveColumnSpec>>._,
                BucketAlignment.CalendarDay, "Europe/Vienna", null))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Create_InvalidReferenceTimeZone_ThrowsArgumentException()
    {
        StubSource(SourceRt);

        await Assert.ThrowsAsync<ArgumentException>(() => NewSut().CreateAsync(
            "BadZone", Sources(SourceRt), TimeSpan.FromDays(1), TimeSpan.FromMinutes(15), VoltageAvg,
            BucketAlignment.CalendarDay, "Europe/Nowhere"));

        MustNotHaveInserted();
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
