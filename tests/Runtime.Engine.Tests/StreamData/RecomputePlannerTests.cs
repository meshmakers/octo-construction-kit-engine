using System;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RecomputePlannerTests
{
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly DateTime EnqueuedAt = new(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static RollupArchiveSnapshot Rollup(
        TimeSpan bucketSize, BucketAlignment alignment = BucketAlignment.FixedSize) =>
        new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(),
            TargetType,
            CkArchiveStatus.Activated,
            null,
            OctoObjectId.GenerateNewId(),
            bucketSize,
            TimeSpan.FromMinutes(5),
            null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null)
        { BucketAlignment = alignment };

    private static ArchiveDirtyWindow Window(
        DateTime start, DateTime end, RecomputeChangeKind kind = RecomputeChangeKind.RetroactiveModify) =>
        new(start, end, kind, RecomputeChangeSource.Pipeline, EnqueuedAt);

    // ---- AlignRangeToBuckets ----------------------------------------------------------------

    [Fact]
    public void AlignRange_SubBucketWindow_ExpandsToOneFixedBucket()
    {
        var (start, end) = RecomputePlanner.AlignRangeToBuckets(
            Utc(2026, 5, 11, 10, 15), Utc(2026, 5, 11, 10, 45),
            BucketAlignment.FixedSize, TimeSpan.FromHours(1));

        Assert.Equal(Utc(2026, 5, 11, 10), start);
        Assert.Equal(Utc(2026, 5, 11, 11), end);
    }

    [Fact]
    public void AlignRange_WindowEndOnBoundary_DoesNotPullInNextBucket()
    {
        var (start, end) = RecomputePlanner.AlignRangeToBuckets(
            Utc(2026, 5, 11, 10), Utc(2026, 5, 11, 12),
            BucketAlignment.FixedSize, TimeSpan.FromHours(1));

        Assert.Equal(Utc(2026, 5, 11, 10), start);
        Assert.Equal(Utc(2026, 5, 11, 12), end);
    }

    [Fact]
    public void AlignRange_EmptyWindow_StillYieldsOneBucket()
    {
        var (start, end) = RecomputePlanner.AlignRangeToBuckets(
            Utc(2026, 5, 11, 10), Utc(2026, 5, 11, 10),
            BucketAlignment.FixedSize, TimeSpan.FromHours(1));

        Assert.Equal(Utc(2026, 5, 11, 10), start);
        Assert.Equal(Utc(2026, 5, 11, 11), end);
    }

    [Fact]
    public void AlignRange_CalendarDay_SnapsToDayBoundaries()
    {
        var (start, end) = RecomputePlanner.AlignRangeToBuckets(
            Utc(2026, 5, 11, 5), Utc(2026, 5, 12, 3),
            BucketAlignment.CalendarDay, TimeSpan.Zero);

        Assert.Equal(Utc(2026, 5, 11), start);
        Assert.Equal(Utc(2026, 5, 13), end);
    }

    // ---- BuildRecomputeRanges ---------------------------------------------------------------

    [Fact]
    public void Build_AppendChange_ProducesNoRanges()
    {
        var window = Window(Utc(2026, 5, 11, 10), Utc(2026, 5, 11, 11), RecomputeChangeKind.Append);
        var dependents = new[] { Rollup(TimeSpan.FromHours(1)) };

        var ranges = RecomputePlanner.BuildRecomputeRanges(window, dependents, EnqueuedAt);

        Assert.Empty(ranges);
    }

    [Fact]
    public void Build_RetroactiveChange_OneRangePerDependentAlignedToOwnBuckets()
    {
        var window = Window(Utc(2026, 5, 11, 10, 15), Utc(2026, 5, 11, 10, 45));
        var hourly = Rollup(TimeSpan.FromHours(1));
        var daily = Rollup(TimeSpan.Zero, BucketAlignment.CalendarDay);

        var ranges = RecomputePlanner.BuildRecomputeRanges(window, new[] { hourly, daily }, EnqueuedAt);

        Assert.Equal(2, ranges.Count);

        var hourlyRange = ranges.Single(r => r.DependentArchiveRtId == hourly.RtId);
        Assert.Equal(Utc(2026, 5, 11, 10), hourlyRange.RangeStart);
        Assert.Equal(Utc(2026, 5, 11, 11), hourlyRange.RangeEnd);
        Assert.Null(hourlyRange.RtIdScope);
        Assert.Equal(EnqueuedAt, hourlyRange.EnqueuedAt);

        var dailyRange = ranges.Single(r => r.DependentArchiveRtId == daily.RtId);
        Assert.Equal(Utc(2026, 5, 11), dailyRange.RangeStart);
        Assert.Equal(Utc(2026, 5, 12), dailyRange.RangeEnd);
    }

    [Fact]
    public void Build_NoDependents_ProducesNoRanges()
    {
        var window = Window(Utc(2026, 5, 11, 10), Utc(2026, 5, 11, 11));

        var ranges = RecomputePlanner.BuildRecomputeRanges(window, Array.Empty<RollupArchiveSnapshot>(), EnqueuedAt);

        Assert.Empty(ranges);
    }

    // ---- MergeIntervals ---------------------------------------------------------------------

    [Fact]
    public void Merge_OverlappingAndAdjacent_Coalesce()
    {
        var merged = RecomputePlanner.MergeIntervals(new[]
        {
            (Utc(2026, 5, 11, 7), Utc(2026, 5, 11, 9)),   // disjoint, given first
            (Utc(2026, 5, 11, 1), Utc(2026, 5, 11, 3)),   // overlaps next
            (Utc(2026, 5, 11, 2), Utc(2026, 5, 11, 5)),
            (Utc(2026, 5, 11, 5), Utc(2026, 5, 11, 6)),   // adjacent to (2,5)
        });

        Assert.Equal(2, merged.Count);
        Assert.Equal((Utc(2026, 5, 11, 1), Utc(2026, 5, 11, 6)), merged[0]);
        Assert.Equal((Utc(2026, 5, 11, 7), Utc(2026, 5, 11, 9)), merged[1]);
    }

    [Fact]
    public void Merge_DegenerateInterval_Dropped()
    {
        var merged = RecomputePlanner.MergeIntervals(new[]
        {
            (Utc(2026, 5, 11, 5), Utc(2026, 5, 11, 5)), // empty
            (Utc(2026, 5, 11, 8), Utc(2026, 5, 11, 9)),
        });

        Assert.Single(merged);
        Assert.Equal((Utc(2026, 5, 11, 8), Utc(2026, 5, 11, 9)), merged[0]);
    }

    [Fact]
    public void Merge_Empty_ReturnsEmpty()
    {
        var merged = RecomputePlanner.MergeIntervals(Array.Empty<(DateTime, DateTime)>());

        Assert.Empty(merged);
    }
}
