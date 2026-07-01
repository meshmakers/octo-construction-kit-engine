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

    // ---- PlanChunks (AB#4283) ----------------------------------------------------------------

    [Fact]
    public void PlanChunks_RangeFitsInOneChunk_ReturnsSingleWholeRange()
    {
        // 5 hourly buckets, chunk cap 10 → one chunk covering the whole range (small-range case).
        var chunks = RecomputePlanner.PlanChunks(
            Utc(2026, 5, 11, 0), Utc(2026, 5, 11, 5), BucketAlignment.FixedSize, TimeSpan.FromHours(1), 10);

        Assert.Single(chunks);
        Assert.Equal((Utc(2026, 5, 11, 0), Utc(2026, 5, 11, 5)), chunks[0]);
    }

    [Fact]
    public void PlanChunks_ManyBuckets_SplitsIntoContiguousBoundedChunks()
    {
        // 5 hourly buckets, chunk cap 2 → [0,2), [2,4), [4,5).
        var chunks = RecomputePlanner.PlanChunks(
            Utc(2026, 5, 11, 0), Utc(2026, 5, 11, 5), BucketAlignment.FixedSize, TimeSpan.FromHours(1), 2);

        Assert.Equal(3, chunks.Count);
        Assert.Equal((Utc(2026, 5, 11, 0), Utc(2026, 5, 11, 2)), chunks[0]);
        Assert.Equal((Utc(2026, 5, 11, 2), Utc(2026, 5, 11, 4)), chunks[1]);
        Assert.Equal((Utc(2026, 5, 11, 4), Utc(2026, 5, 11, 5)), chunks[2]);
    }

    [Fact]
    public void PlanChunks_ChunksAreContiguousCoverTheWholeRangeAndDoNotSplitBuckets()
    {
        var from = Utc(2026, 5, 11, 0);
        var to = Utc(2026, 5, 11, 7); // 7 hourly buckets
        var bucket = TimeSpan.FromHours(1);

        var chunks = RecomputePlanner.PlanChunks(from, to, BucketAlignment.FixedSize, bucket, 3);

        // Contiguous, no gaps or overlaps: first starts at from, last ends at to, each chunk's end
        // equals the next chunk's start.
        Assert.Equal(from, chunks[0].Start);
        Assert.Equal(to, chunks[^1].End);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].End, chunks[i].Start);
        }

        // Every chunk boundary lands on a whole-bucket boundary (no bucket is split) and no chunk
        // exceeds the cap of 3 buckets.
        foreach (var (start, end) in chunks)
        {
            Assert.Equal(0, (start - from).Ticks % bucket.Ticks);
            Assert.Equal(0, (end - from).Ticks % bucket.Ticks);
            var buckets = (int)((end - start).Ticks / bucket.Ticks);
            Assert.InRange(buckets, 1, 3);
        }

        // The chunks tile exactly [from, to): total buckets == 7.
        Assert.Equal(7, chunks.Sum(c => (int)((c.End - c.Start).Ticks / bucket.Ticks)));
    }

    [Fact]
    public void PlanChunks_CalendarMonthAlignment_StepsWholeMonths()
    {
        // 5 calendar months, cap 2 → [Jan,Mar), [Mar,May), [May,Jun).
        var chunks = RecomputePlanner.PlanChunks(
            Utc(2026, 1, 1), Utc(2026, 6, 1), BucketAlignment.CalendarMonth, TimeSpan.FromDays(30), 2);

        Assert.Equal(3, chunks.Count);
        Assert.Equal((Utc(2026, 1, 1), Utc(2026, 3, 1)), chunks[0]);
        Assert.Equal((Utc(2026, 3, 1), Utc(2026, 5, 1)), chunks[1]);
        Assert.Equal((Utc(2026, 5, 1), Utc(2026, 6, 1)), chunks[2]);
    }

    [Fact]
    public void PlanChunks_InvertedOrEmptyRange_ReturnsEmpty()
    {
        Assert.Empty(RecomputePlanner.PlanChunks(
            Utc(2026, 5, 11, 5), Utc(2026, 5, 11, 5), BucketAlignment.FixedSize, TimeSpan.FromHours(1), 4));
        Assert.Empty(RecomputePlanner.PlanChunks(
            Utc(2026, 5, 11, 6), Utc(2026, 5, 11, 5), BucketAlignment.FixedSize, TimeSpan.FromHours(1), 4));
    }

    [Fact]
    public void PlanChunks_NonPositiveChunkSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecomputePlanner.PlanChunks(
            Utc(2026, 5, 11, 0), Utc(2026, 5, 11, 5), BucketAlignment.FixedSize, TimeSpan.FromHours(1), 0));
    }

    [Fact]
    public void PlanChunks_DecadeHourlyRange_TilesEveryBucketExactlyOnce()
    {
        // Regression guard for the AB#4283 use case: ~10y of hourly buckets chunked at the default
        // 2000 must tile the whole range with contiguous, non-overlapping chunks.
        var from = Utc(2016, 1, 1);
        var to = Utc(2026, 1, 1);
        var bucket = TimeSpan.FromHours(1);
        var expectedBuckets = (int)((to - from).Ticks / bucket.Ticks);

        var chunks = RecomputePlanner.PlanChunks(from, to, BucketAlignment.FixedSize, bucket, 2000);

        Assert.Equal(from, chunks[0].Start);
        Assert.Equal(to, chunks[^1].End);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].End, chunks[i].Start);
        }
        Assert.All(chunks, c => Assert.InRange((int)((c.End - c.Start).Ticks / bucket.Ticks), 1, 2000));
        Assert.Equal(expectedBuckets, chunks.Sum(c => (int)((c.End - c.Start).Ticks / bucket.Ticks)));
    }
}
