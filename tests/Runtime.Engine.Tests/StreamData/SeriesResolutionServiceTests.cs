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
    private static readonly DateTime MidYear = new(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);
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
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(sourceRtId) },
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
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(sourceRtId) },
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
        // Bucket snaps to a whole multiple of the 1 h grain (AB#4714): 8760 h / round(14.6) = 584.
        Assert.Equal(584, result.Points);
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
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(baseArchive.RtId) },
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

    // ---- AB#4336: cascade function-matching via RollupLadderFunctionResolver ----

    [Fact]
    public async Task CascadeRollup_TwaPairChainedViaSum_MatchedForTimeWeightedAvg()
    {
        // Hourly materialises TWA over the base; Daily accumulates the pair via SUM specs on the
        // hourly physical columns. The daily rung must now be a valid TWA source (Phase-1
        // limitation lifted) — and being coarser AND fine enough for the year window, it wins.
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var hourly = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(baseArchive.RtId) },
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("DimmingLevel", CkRollupFunction.TimeWeightedAvg, null) }, null);
        var daily = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(hourly.RtId) },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), null,
            new[]
            {
                new CkRollupAggregationSpec("dimminglevel_twavg_integral", CkRollupFunction.Sum, "dimminglevel_twavg_integral"),
                new CkRollupAggregationSpec("dimminglevel_twavg_duration", CkRollupFunction.Sum, "dimminglevel_twavg_duration"),
            }, null);
        StubRollups(baseArchive.RtId, hourly, daily);

        var request = new SeriesResolutionRequest(
            baseArchive.RtId, null, YearFrom, YearTo, 300, CkRollupFunction.TimeWeightedAvg, "DimmingLevel");
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(daily.RtId, result.ArchiveRtId);
    }

    [Fact]
    public async Task CascadeRollup_IncompleteTwaPair_NotMatched()
    {
        // Daily only accumulates the integral half — the pair cannot be recombined, so the daily
        // rung must not satisfy a TWA request (the hourly rung still does).
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var hourly = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(baseArchive.RtId) },
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("DimmingLevel", CkRollupFunction.TimeWeightedAvg, null) }, null);
        var brokenDaily = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null, new[] { new RollupSourceReference(hourly.RtId) },
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), null,
            new[]
            {
                new CkRollupAggregationSpec("dimminglevel_twavg_integral", CkRollupFunction.Sum, "dimminglevel_twavg_integral"),
            }, null);
        StubRollups(baseArchive.RtId, hourly, brokenDaily);

        var request = new SeriesResolutionRequest(
            baseArchive.RtId, null, YearFrom, YearTo, 300, CkRollupFunction.TimeWeightedAvg, "DimmingLevel");
        var result = await NewSut().ResolveAsync(request, TestContext.Current.CancellationToken);

        // Ideal bucket for 365 d / 300 points is ~1.2 d: the broken daily rung would have been the
        // coarsest sufficient rung — with it excluded, the hourly TWA rung is chosen instead.
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(hourly.RtId, result.ArchiveRtId);
    }
    // ---- AB#5157: measured coverage (optional provider) --------------------------------------

    private readonly IArchiveCoverageProvider _coverageProvider = A.Fake<IArchiveCoverageProvider>();

    private SeriesResolutionService NewSutWithCoverage() =>
        new(_archiveStore, _dependencyGraph, _coverageProvider);

    private void StubCoverage(OctoObjectId rtId, DateTime? availableFrom, DateTime? availableTo = null) =>
        A.CallTo(() => _coverageProvider.GetCoverageAsync(rtId, A<System.Threading.CancellationToken>._))
            .Returns(availableFrom is { } from ? new ArchiveCoverage(from, availableTo ?? YearTo) : null);

    /// Base (15 min) + an hourly and a daily SUM rollup directly over it, all stubbed on the stores.
    private (ArchiveSnapshot BaseArchive, RollupArchiveSnapshot Hourly, RollupArchiveSnapshot Daily) CoverageLadder()
    {
        var baseArchive = Base(TimeSpan.FromMinutes(15));
        StubBase(baseArchive);
        var hourly = Rollup(baseArchive.RtId, TimeSpan.FromHours(1), CkRollupFunction.Sum);
        var daily = Rollup(baseArchive.RtId, TimeSpan.FromDays(1), CkRollupFunction.Sum);
        StubRollups(baseArchive.RtId, hourly, daily);
        return (baseArchive, hourly, daily);
    }

    [Fact]
    public async Task WithoutACoverageProvider_TheFilterIsInertAndTheHourlyRungIsChosen()
    {
        var (baseArchive, hourly, _) = CoverageLadder();

        var result = await NewSut().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(hourly.RtId, result.ArchiveRtId);
        Assert.Null(result.FinerRungAvailableFrom);
        A.CallTo(() => _coverageProvider.GetCoverageAsync(A<OctoObjectId>._, A<System.Threading.CancellationToken>._))
            .MustNotHaveHappened();
    }

    // TC-RES-05: the hourly rung holds no data for the requested start; the covering daily rung
    // answers instead and the result says so.
    [Fact]
    public async Task FinestRungStartsAfterTheRequestedStart_FallsBackToTheCoveringRung_WithCoverageLimited()
    {
        var (baseArchive, hourly, daily) = CoverageLadder();
        StubCoverage(baseArchive.RtId, YearFrom);
        StubCoverage(hourly.RtId, MidYear);
        StubCoverage(daily.RtId, YearFrom);

        var result = await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.CoverageLimited, result.Signal);
        Assert.Equal(daily.RtId, result.ArchiveRtId);
    }

    // TC-RES-08: ActualPoints carries what the selected rung really yields.
    [Fact]
    public async Task CoverageLimitedResult_CarriesActualPoints()
    {
        var (baseArchive, hourly, daily) = CoverageLadder();
        StubCoverage(baseArchive.RtId, YearFrom);
        StubCoverage(hourly.RtId, MidYear);
        StubCoverage(daily.RtId, YearFrom);

        var result = await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(365, result.Points);       // 365 daily buckets in the year
        Assert.Equal(365, result.ActualPoints);
    }

    // TC-RES-09: the diagnostic names the excluded finer rung and its available-from, and the same
    // instant is exposed as the first-class FinerRungAvailableFrom field.
    [Fact]
    public async Task CoverageLimitedResult_DiagnosticNamesTheExcludedRungAndItsAvailableFrom()
    {
        var (baseArchive, hourly, daily) = CoverageLadder();
        StubCoverage(baseArchive.RtId, YearFrom);
        StubCoverage(hourly.RtId, MidYear);
        StubCoverage(daily.RtId, YearFrom);

        var result = await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.NotNull(result.Diagnostic);
        Assert.Contains(hourly.RtId.ToString(), result.Diagnostic!);
        Assert.Contains(MidYear.ToString("O"), result.Diagnostic!);
        Assert.Equal(MidYear, result.FinerRungAvailableFrom);
    }

    // TC-X-RES-01: a requested start exactly at the finest rung's available-from selects that rung
    // and raises no signal — the boundary is inclusive on filter and signal alike.
    [Fact]
    public async Task RequestedStartEqualToTheFinestRungsAvailableFrom_SelectsItWithoutASignal()
    {
        var (baseArchive, hourly, daily) = CoverageLadder();
        StubCoverage(baseArchive.RtId, YearFrom - TimeSpan.FromDays(1));
        StubCoverage(hourly.RtId, YearFrom);
        StubCoverage(daily.RtId, YearFrom - TimeSpan.FromDays(1));

        var result = await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(hourly.RtId, result.ArchiveRtId);
        Assert.Null(result.FinerRungAvailableFrom);
    }

    [Fact]
    public async Task CoverageProvider_IsAskedExactlyOncePerRungIncludingTheBase()
    {
        var (baseArchive, hourly, daily) = CoverageLadder();
        StubCoverage(baseArchive.RtId, YearFrom);
        StubCoverage(hourly.RtId, YearFrom);
        StubCoverage(daily.RtId, YearFrom);

        await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        A.CallTo(() => _coverageProvider.GetCoverageAsync(baseArchive.RtId, A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _coverageProvider.GetCoverageAsync(hourly.RtId, A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _coverageProvider.GetCoverageAsync(daily.RtId, A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ProviderReportsNoCoverageAnywhere_KeepsTheFilterInert()
    {
        var (baseArchive, hourly, _) = CoverageLadder();
        A.CallTo(() => _coverageProvider.GetCoverageAsync(A<OctoObjectId>._, A<System.Threading.CancellationToken>._))
            .Returns((ArchiveCoverage?)null);

        var result = await NewSutWithCoverage().ResolveAsync(
            Request(baseArchive.RtId, YearFrom, YearTo, 600), TestContext.Current.CancellationToken);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(hourly.RtId, result.ArchiveRtId);
    }
}
