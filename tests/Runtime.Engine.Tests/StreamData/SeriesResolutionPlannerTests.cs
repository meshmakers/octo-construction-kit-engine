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
        new(OctoObjectId.GenerateNewId(), grainMs, BucketAlignment.FixedSize, null, IsBase: true);

    private static ResolutionRung Rollup(long grainMs, CkRollupFunction fn) =>
        new(OctoObjectId.GenerateNewId(), grainMs, BucketAlignment.FixedSize, fn, IsBase: false);

    private static SeriesResolutionResult Plan(
        IReadOnlyList<ResolutionRung> ladder, DateTime from, DateTime to, int target, CkRollupFunction agg) =>
        SeriesResolutionPlanner.Plan(ladder, from, to, target, agg, FixedProbe);

    // ---- Worked example --------------------------------------------------------------------

    [Fact]
    public void WorkedExample_YearTo600Points_PicksCoarsestSufficientRollup_OneHour()
    {
        // 1 year / 600 points → ideal bucket ≈ 14.6 h. Rungs: raw 15 min (base) + 1 h + 1 d SUM rollups.
        // 1 d (24 h) is too coarse; 1 h is the coarsest rung still ≤ ideal → chosen, downsampled to 600.
        var oneHour = Rollup(OneHour, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), oneHour, Rollup(OneDay, CkRollupFunction.Sum) };

        var result = Plan(ladder, YearFrom, YearTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.Ok, result.Signal);
        Assert.Equal(oneHour.ArchiveRtId, result.ArchiveRtId);
        Assert.Equal(600, result.Points);
        Assert.Equal(52_560_000, result.EffectiveBucketMs); // 365 d / 600
        Assert.Equal(CkRollupFunction.Sum, result.ReducingFunction);
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
    public void OnlyDailyRollup_OneDayTo600_ResolutionLimited_SinglePoint()
    {
        var daily = Rollup(OneDay, CkRollupFunction.Sum);
        var ladder = new[] { Base(FifteenMin), daily };

        var result = Plan(ladder, DayFrom, DayTo, 600, CkRollupFunction.Sum);

        Assert.Equal(SeriesResolutionSignal.ResolutionLimited, result.Signal);
        Assert.Equal(1, result.Points);
        Assert.Equal(1, result.ActualPoints);
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
}
