using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class SeriesResolutionPlannerTests
{
    private const long FifteenMin = 15L * 60 * 1000;
    private const long OneHour = 60L * 60 * 1000;
    private const long SixHours = 6 * OneHour;
    private const long OneDay = 24 * OneHour;

    private static readonly DateTime YearFrom = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime YearTo = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); // 365 days
    private static readonly DateTime DayFrom = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayTo = new(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc);

    // Probe for FixedSize rungs: the effective grain IS the declared grain (null stays null).
    private static long? FixedProbe(ResolutionRung r, DateTime from, DateTime to) => r.GrainMs;

    private static ResolutionRung Base(long? grainMs) =>
        new(OctoObjectId.GenerateNewId(), grainMs, BucketAlignment.FixedSize,
            Array.Empty<CkRollupFunction>(), IsBase: true);

    private static ResolutionRung Rollup(long grainMs, params CkRollupFunction[] fns) =>
        new(OctoObjectId.GenerateNewId(), grainMs, BucketAlignment.FixedSize, fns, IsBase: false);

    private static SeriesResolutionResult Plan(
        IReadOnlyList<ResolutionRung> ladder, DateTime from, DateTime to, int target, CkRollupFunction agg) =>
        SeriesResolutionPlanner.Plan(ladder, from, to, target, agg, FixedProbe);

    // ---- Worked example --------------------------------------------------------------------

    [Fact]
    public void WorkedExample_YearTo600Points_PicksCoarsestSufficientRollup_OneHour()
    {
        // 1 year / 600 points → ideal bucket ≈ 14.6 h. Rungs: raw 15 min (base) + 1 h + 1 d SUM rollups.
        // 1 d (24 h) is too coarse; 1 h is the coarsest rung still ≤ ideal → chosen. The output bucket is
        // snapped to an integer multiple of the 1 h grain (a windowed rollup can only be summed on whole
        // grain windows, AB#4714): merge = round(14.6) = 15 → 15 h buckets, 8760 h / 15 = 584 points.
        var oneHour = Rollup(OneHour, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), oneHour, Rollup(OneDay, CkRollupFunction.Sum) };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHour.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(584, result.Points);
        Assert.Equal(15 * OneHour, result.EffectiveBucketMs); // 15 h — nearest 1 h multiple to the 14.6 h ideal
        Assert.Equal(CkRollupFunction.Sum, result.ReducingFunction);
    }

    [Fact]
    public void MonthTo670Points_OverHourlyRollup_ReadsNativeGrain_NotPixelBucket()
    {
        // The AB#4714 regression: a 30-day month at ~670 pixels → ideal bucket ≈ 1.07 h. The hourly
        // rollup is the coarsest rung ≤ ideal. merge = round(1.07) = 1, so the bucket snaps to the 1 h
        // grain (NOT the 1.07 h pixel width, which is not a grain multiple and dropped ~94% of windows).
        var from = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc); // 30 days = 720 h
        var oneHour = Rollup(OneHour, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), oneHour, Rollup(OneDay, CkRollupFunction.Sum) };

        var result = Plan(ladder, from, to, 670, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHour.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(OneHour, result.EffectiveBucketMs); // native 1 h grain, grid-aligned → lossless
        Assert.Equal(720, result.Points);                // 720 h / 1 h, not the requested 670
    }

    [Fact]
    public void CoarsestSufficient_PrefersCoarserRungOverFiner()
    {
        // Both 1 h and 6 h are ≤ ideal (14.6 h); the coarsest (6 h, least scan) must win.
        var sixHours = Rollup(SixHours, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), Rollup(OneHour, CkRollupFunction.Sum), sixHours };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(sixHours.ArchiveRtId, result.ArchiveRtId);
    }

    [Fact]
    public void Boundary_IdealEqualsRungGrain_PicksThatRung_Inclusive()
    {
        // span = 1 day, target = 24 → ideal = exactly 1 h. The ≤ comparison is inclusive, so the
        // 1 h rung is fine-enough and chosen (not the finer base).
        var oneHour = Rollup(OneHour, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), oneHour, Rollup(OneDay, CkRollupFunction.Sum) };

        var result = Plan(ladder, DayFrom, DayTo, 24, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHour.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(24, result.Points);
        Assert.Equal(OneHour, result.EffectiveBucketMs);
    }

    // ---- Resolution-limited (O4) -----------------------------------------------------------

    [Fact]
    public void OnlyDailyRollup_YearTo600_ResolutionLimited_365Points()
    {
        var daily = Rollup(OneDay, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), daily };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.ResolutionLimited, result.Signal);
        Assert.Equal(daily.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(365, result.Points);
        Assert.Equal(365, result.ActualPoints);
        Assert.Equal(OneDay, result.EffectiveBucketMs);
    }

    [Fact]
    public void ShortWindow_BaseFits_PreferredOverCoarserRollup()
    {
        // 1 day of 15-min data = 96 points <= 600. No rollup is fine enough (daily/hourly are coarser
        // than the ~2.4-min ideal). The base (96 points, finest) must win over a ResolutionLimited
        // coarse rollup (hourly would give only 24, daily only 1) — raw-fits is checked first.
        var baseRung = Base(FifteenMin);
        var ladder = new[] { baseRung, Rollup(OneHour, CkRollupFunction.Sum), Rollup(OneDay, CkRollupFunction.Sum) };

        var result = Plan(ladder, DayFrom, DayTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(96, result.Points);
        Assert.Equal(FifteenMin, result.EffectiveBucketMs);
    }

    // ---- O2 compatibility + refuse (O2-followup) -------------------------------------------

    [Fact]
    public void IncompatibleFunction_OnlyAvgRollups_ForSumSeries_RefusesWithSignal()
    {
        // Additive (SUM) series, but the only rollups store AVG → not a valid reduction source.
        // The base is not reduced directly → refuse and return the base with a NoSuitableRollup signal.
        var baseRung = Base(FifteenMin);
        var ladder = new[]
        {
            baseRung,
            Rollup(OneHour, CkRollupFunction.Avg),
            Rollup(OneDay, CkRollupFunction.Avg),
        };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(35_040, result.Points);      // 365 d / 15 min
        Assert.Equal(35_040, result.ActualPoints);
    }

    [Fact]
    public void CompatibleSumRollupExists_PicksIt_IgnoringAvgSibling()
    {
        var oneHourSum = Rollup(OneHour, CkRollupFunction.Sum);
        var ladder = new[]
        {
            Base(FifteenMin),
            Rollup(OneHour, CkRollupFunction.Avg), // same grain, wrong function → ignored
            oneHourSum,
        };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHourSum.ArchiveRtId, result.ArchiveRtId);
    }

    [Fact]
    public void MultiAggregationRollup_RequestedFunctionAmongStored_Eligible()
    {
        // AB#4336 regression: a rollup carrying AVG *and* MAX on the same source path must match a
        // MAX request regardless of the order the aggregations were declared in.
        var avgMax = Rollup(OneHour, CkRollupFunction.Avg, CkRollupFunction.Max);
        var ladder = new[] { Base(FifteenMin), avgMax };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Max);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(avgMax.ArchiveRtId, result.ArchiveRtId);
    }

    [Fact]
    public void MultiAggregationRollup_RequestedFunctionNotStored_Ineligible()
    {
        // AVG+MAX rollup must still NOT satisfy a SUM request.
        var baseRung = Base(FifteenMin);
        var ladder = new[] { baseRung, Rollup(OneHour, CkRollupFunction.Avg, CkRollupFunction.Max) };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
    }

    [Fact]
    public void NoRollup_BaseFitsWithinTarget_NoReductionNeeded_ReturnsBase()
    {
        // 1 day of 15-min data = 96 points ≤ 600 → no reduction needed, return the base directly.
        var baseRung = Base(FifteenMin);

        var result = Plan(new[] { baseRung }, DayFrom, DayTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(96, result.Points);
        Assert.Equal(FifteenMin, result.EffectiveBucketMs);
    }

    [Fact]
    public void NoRollup_BaseExceedsTarget_RefusesWithSignal()
    {
        // 1 year of 15-min data = 35 040 points > 600, no rollup to reduce it, base not reduced → refuse.
        var baseRung = Base(FifteenMin);

        var result = Plan(new[] { baseRung }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.NoSuitableRollup, result.Signal);
        Assert.Equal(35_040, result.ActualPoints);
    }

    // ---- Unknown base grain / empty --------------------------------------------------------

    [Fact]
    public void BaseGrainUnknown_NoRollup_UnknownBaseGrain()
    {
        // Raw base with no declared Period → cannot tell whether reduction is needed.
        var baseRung = Base(null);

        var result = Plan(new[] { baseRung }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.UnknownBaseGrain, result.Signal);
        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(0, result.Points);
    }

    [Fact]
    public void EmptyLadder_ReturnsEmptyLadderSignal()
    {
        var result = Plan(Array.Empty<ResolutionRung>(), YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.EmptyLadder, result.Signal);
    }

    [Fact]
    public void NonPositiveTargetPoints_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Plan(new[] { Base(FifteenMin) }, YearFrom, YearTo, 0, CkRollupFunction.Sum));
    }
    // ---- AB#5157: measured coverage filter ---------------------------------------------------

    private const long TwelveHours = 12 * OneHour;

    private static readonly DateTime BeforeYear = YearFrom - TimeSpan.FromDays(1);
    private static readonly DateTime MidYear = new(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ResolutionRung From(ResolutionRung rung, DateTime? availableFrom, DateTime? availableTo = null) =>
        rung with { AvailableFrom = availableFrom, AvailableTo = availableTo };

    // TC-RES-01: the finest rung starts after the requested start, so the coverage filter drops it
    // before the selection rule runs and the covering daily rung answers the request.
    [Fact]
    public void CoverageFilter_FinestRungStartsAfterTheRequestedStart_TheCoveringCoarserRungIsChosen()
    {
        var baseRung = From(Base(FifteenMin), YearFrom);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), MidYear);
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), YearFrom);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        // Without the filter the hourly rung would have won the point/grain rule.
        Assert.Equal(daily.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(365, result.Points);
    }

    // TC-RES-07: the filtered plan is itself resolution-limited; CoverageLimited takes precedence.
    [Fact]
    public void CoverageFilter_ChangedOutcome_ReportsCoverageLimitedOverResolutionLimited()
    {
        var baseRung = From(Base(FifteenMin), YearFrom);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), MidYear);
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), YearFrom);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.CoverageLimited, result.Signal);
        Assert.Equal(365, result.ActualPoints);
        Assert.Contains(hourly.ArchiveRtId.ToString(), result.Diagnostic!);
        Assert.Contains(MidYear.ToString("O"), result.Diagnostic!);
        Assert.Equal(MidYear, result.FinerRungAvailableFrom);
    }

    // TC-RES-02: available-from exactly at the requested start still covers (at-or-before).
    [Fact]
    public void CoverageFilter_AvailableFromEqualToTheRequestedStart_CountsAsCovering()
    {
        var baseRung = From(Base(FifteenMin), BeforeYear);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), YearFrom);
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), BeforeYear);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        // A strict comparison would have excluded the hourly rung and fallen back to daily.
        Assert.Equal(hourly.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Null(result.FinerRungAvailableFrom);
    }

    // TC-RES-03: the requested END plays no part in the filter.
    [Fact]
    public void CoverageFilter_AvailableToBeforeTheRequestedEnd_KeepsTheRungEligible()
    {
        var baseRung = From(Base(FifteenMin), BeforeYear, YearTo);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), BeforeYear,
            new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), BeforeYear, YearTo);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(hourly.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
    }

    // TC-RES-04: no rung covers the start ⇒ every rung sharing the earliest available-from stays a
    // candidate and the existing rule picks among them.
    [Fact]
    public void CoverageFilter_NoCoveringRung_KeepsEveryRungTiedOnTheEarliestAvailableFrom()
    {
        var march = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var june = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseRung = From(Base(FifteenMin), june);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), march);
        var sixHours = From(Rollup(SixHours, CkRollupFunction.Sum), march);
        var twelveHours = From(Rollup(TwelveHours, CkRollupFunction.Sum), june);

        var result = Plan(new[] { baseRung, hourly, sixHours, twelveHours }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        // Both March rungs remain candidates; the coarsest sufficient of the two wins. The 12 h rung
        // (the unfiltered choice) starts too late to be a candidate.
        Assert.Equal(sixHours.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.CoverageLimited, result.Signal);
    }

    // TC-RES-06: when the filter changes nothing the pre-AB#5157 answer stands.
    [Fact]
    public void CoverageFilter_EveryRungCoversTheStart_LeavesTheAnswerUnchanged()
    {
        var baseRung = Base(FifteenMin);
        var hourly = Rollup(OneHour, CkRollupFunction.Sum);
        var daily = Rollup(OneDay, CkRollupFunction.Sum);
        var expected = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        var result = Plan(
            new[] { From(baseRung, BeforeYear), From(hourly, BeforeYear), From(daily, BeforeYear) },
            YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(expected, result);
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
    }

    // TC-X-RES-02: the filter is not restricted to rollup rungs — the base can be the only covering
    // rung and is then returned by the unchanged rule 2.
    [Fact]
    public void CoverageFilter_OnlyTheBaseCoversTheStart_ReturnsTheBaseWithCoverageLimited()
    {
        var baseRung = From(Base(FifteenMin), DayFrom - TimeSpan.FromDays(1));
        var quarterHourly = From(Rollup(FifteenMin, CkRollupFunction.Sum), DayFrom + TimeSpan.FromHours(12));

        var result = Plan(new[] { baseRung, quarterHourly }, DayFrom, DayTo, 96, CkRollupFunction.Sum);

        Assert.Equal(baseRung.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.CoverageLimited, result.Signal);
        Assert.Equal(96, result.Points);
        Assert.Contains(quarterHourly.ArchiveRtId.ToString(), result.Diagnostic!);
    }

    // TC-X-RES-03: all rungs share one available-from and none covers the start — the outcome does
    // not change, so no signal is raised.
    [Fact]
    public void CoverageFilter_AllRungsShareTheSameAvailableFrom_KeepsTheSignalUnchanged()
    {
        var march = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseRung = From(Base(FifteenMin), march);
        var hourly = From(Rollup(OneHour, CkRollupFunction.Sum), march);
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), march);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(hourly.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
    }

    // TC-X-RES-04: an absent coverage is "covers nothing", never "covers everything".
    [Fact]
    public void CoverageFilter_RungWithoutAnyCoverage_IsExcludedWhileOtherRungsHaveCoverage()
    {
        var baseRung = From(Base(FifteenMin), YearFrom);
        var neverPopulatedHourly = Rollup(OneHour, CkRollupFunction.Sum); // AvailableFrom stays null
        var daily = From(Rollup(OneDay, CkRollupFunction.Sum), YearFrom);

        var result = Plan(new[] { baseRung, neverPopulatedHourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(daily.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.CoverageLimited, result.Signal);
        Assert.Contains(neverPopulatedHourly.ArchiveRtId.ToString(), result.Diagnostic!);
        Assert.Null(result.FinerRungAvailableFrom); // the excluded rung reports no coverage at all
    }

    // No rung reports coverage at all ⇒ the filter is inert and the pre-AB#5157 result is returned.
    [Fact]
    public void CoverageFilter_NoRungReportsCoverage_IsInert()
    {
        var baseRung = Base(FifteenMin);
        var hourly = Rollup(OneHour, CkRollupFunction.Sum);
        var daily = Rollup(OneDay, CkRollupFunction.Sum);

        var result = Plan(new[] { baseRung, hourly, daily }, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(hourly.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Null(result.FinerRungAvailableFrom);
    }
}
