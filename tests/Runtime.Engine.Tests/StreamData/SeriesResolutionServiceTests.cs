using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class SeriesResolutionServiceTests
{
    private const string Path = "Amount.Value";
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("EnergyMeasurement"));
    private static readonly DateTime YearFrom = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime YearTo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayFrom = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayTo = new(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupDependencyGraph _dependencyGraph = A.Fake<IRollupDependencyGraph>();

    private SeriesResolutionService NewSut() => new(_archiveStore, _dependencyGraph);

    private static ArchiveSnapshot Base(TimeSpan? period) =>
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null,
            Array.Empty<CkArchiveColumnSpec>())
        {
            IsTimeRange = period is not null,
            Period = period,
        };

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId sourceRtId, TimeSpan bucketSize, CkRollupFunction fn, string path = Path) =>
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, sourceRtId,
            bucketSize, TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec(path, fn, null) }, null);

    private void StubBase(ArchiveSnapshot snapshot) =>
        A.CallTo(() => _archiveStore.GetAsync(snapshot.RtId)).Returns(snapshot);

    private void StubRollups(OctoObjectId baseRtId, params RollupArchiveSnapshot[] rollups) =>
        A.CallTo(() => _dependencyGraph.GetTransitiveDependentsAsync(baseRtId))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)rollups);

    private static SeriesResolutionRequest Request(OctoObjectId baseRtId, DateTime from, DateTime to, int target) =>
        new(baseRtId, null, from, to, target, CkRollupFunction.Sum, Path);

    private static RollupArchiveSnapshot CalendarRollup(
        OctoObjectId sourceRtId, BucketAlignment alignment, string? tz,
        CkRollupFunction fn = CkRollupFunction.Sum, string path = Path) =>
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, sourceRtId,
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec(path, fn, null) }, null)
        {
            BucketAlignment = alignment,
            ReferenceTimeZone = tz,
        };

    [Fact]
    public async Task SingleStepSumRollup_Matched_PicksIt()
    {
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var oneHour = Rollup(baseArchive.RtId, TimeSpan.FromHours(1), CkRollupFunction.Sum);
        StubRollups(baseArchive.RtId, oneHour);

        var result = await NewSut().ResolveAsync(Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHour.RtId, result.ArchiveRtId);
        Assert.Equal(600, result.Points);
    }

    [Fact]
    public async Task MultiAggregationRollup_LaterFunctionRequested_Matched()
    {
        // AB#4336 regression: the rollup declares AVG *before* MAX on the same source path. The old
        // first-match logic returned AVG and the MAX request fell through to "no compatible rollup";
        // all functions of the path must be considered.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var avgMax = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, baseArchive.RtId,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null,
            new[]
            {
                new CkRollupAggregationSpec(Path, CkRollupFunction.Avg, null),
                new CkRollupAggregationSpec(Path, CkRollupFunction.Max, null),
            }, null);
        StubRollups(baseArchive.RtId, avgMax);

        var request = Request(baseArchive.RtId, YearFrom, YearTo, 600) with
        {
            RequiredAggregation = CkRollupFunction.Max,
        };
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(avgMax.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task CascadeRollup_FunctionUnknown_ExcludedFromSelection()
    {
        // A rollup whose source is another rollup (not the base) is not matched in Phase 1 → ineligible.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var cascade = Rollup(OctoObjectId.GenerateNewId(), TimeSpan.FromHours(1), CkRollupFunction.Sum);
        StubRollups(baseArchive.RtId, cascade);

        var result = await NewSut().ResolveAsync(Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        // No eligible rollup, base (15 min) would yield 35 040 > 600 → refuse.
        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
        Assert.Equal(baseArchive.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task SingleStepRollup_DifferentPath_NotMatched()
    {
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var wrongPath = Rollup(baseArchive.RtId, TimeSpan.FromHours(1), CkRollupFunction.Sum, path: "Other.Column");
        StubRollups(baseArchive.RtId, wrongPath);

        var result = await NewSut().ResolveAsync(Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
    }

    [Fact]
    public async Task NoRollups_BaseGrainFromPeriod_FitsWithinTarget_ReturnsBase()
    {
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        StubRollups(baseArchive.RtId); // none

        // 1 day of 15-min data = 96 points ≤ 600 → base returned, no reduction needed.
        var result = await NewSut().ResolveAsync(
            Request(baseArchive.RtId, DayFrom, DayTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(baseArchive.RtId, result.ArchiveRtId);
        Assert.Equal(96, result.Points);
    }

    [Fact]
    public async Task NoRollups_BasePeriodUndeclared_UnknownBaseGrain()
    {
        var baseArchive = Base(period: null); // raw, no declared grain
        StubBase(baseArchive);
        StubRollups(baseArchive.RtId);

        var result = await NewSut().ResolveAsync(Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.UnknownBaseGrain, result.Signal);
    }

    [Fact]
    public async Task MissingBaseArchive_EmptyLadder()
    {
        var missing = OctoObjectId.GenerateNewId();
        A.CallTo(() => _archiveStore.GetAsync(missing)).Returns((ArchiveSnapshot?)null);

        var result = await NewSut().ResolveAsync(
            Request(missing, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.EmptyLadder, result.Signal);
    }

    // ---------- AB#4190: timezone-aware resolution (decisions T2 / T3) ----------

    [Fact]
    public async Task PerQuery_CalendarRollup_ZoneMatchesQuery_Selected()
    {
        // 365 days, target 200 → ideal bucket ≈ 1.8 d, so a 1-day calendar rung is fine enough.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var dailyVienna = CalendarRollup(baseArchive.RtId, BucketAlignment.CalendarDay, "Europe/Vienna");
        StubRollups(baseArchive.RtId, dailyVienna);

        var request = Request(baseArchive.RtId, YearFrom, YearTo, 200) with { QueryTimeZone = "Europe/Vienna" };
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(dailyVienna.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task PerQuery_CalendarRollup_ZoneMismatch_ExcludedFromSelection()
    {
        // Same daily rollup, but stored in Vienna while the query asks for a New York civil day —
        // the stored buckets are a different zone's civil days, so the rung is not a valid source.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var dailyVienna = CalendarRollup(baseArchive.RtId, BucketAlignment.CalendarDay, "Europe/Vienna");
        StubRollups(baseArchive.RtId, dailyVienna);

        var request = Request(baseArchive.RtId, YearFrom, YearTo, 200) with { QueryTimeZone = "America/New_York" };
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        // Calendar rung excluded → no eligible rollup, 15-min base (35 040 pts) > 200 → refuse.
        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
        Assert.Equal(baseArchive.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task PerSeries_CalendarRollup_UsesOwnZone_RegardlessOfQueryZone()
    {
        // Under PerSeries the query zone is ignored; the calendar rung aligns to its own stored zone
        // and is eligible even when the query zone differs.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var dailyVienna = CalendarRollup(baseArchive.RtId, BucketAlignment.CalendarDay, "Europe/Vienna");
        StubRollups(baseArchive.RtId, dailyVienna);

        var request = Request(baseArchive.RtId, YearFrom, YearTo, 200) with
        {
            QueryTimeZone = "America/New_York",
            ComparisonPolicy = SeriesComparisonPolicy.PerSeries,
        };
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(dailyVienna.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task NoBaseRtId_EmptyLadder()
    {
        var request = new SeriesResolutionRequest(
            BaseArchiveRtId: null, TargetCkTypeId: null, YearFrom, YearTo, 600, CkRollupFunction.Sum, Path);

        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.EmptyLadder, result.Signal);
    }
}
