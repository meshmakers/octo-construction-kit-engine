using System;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class BucketBoundaryTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    // ---- NextBucketEnd ---------------------------------------------------------------------

    [Fact]
    public void NextBucketEnd_FixedSize_AddsBucketSize()
    {
        var start = new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(start, BucketAlignment.FixedSize, OneHour);

        Assert.Equal(new DateTime(2026, 5, 11, 15, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void NextBucketEnd_CalendarDay_AddsOneDay()
    {
        var start = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(start, BucketAlignment.CalendarDay, OneHour);

        Assert.Equal(new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void NextBucketEnd_Iso8601Week_AddsSevenDays()
    {
        // 2026-05-11 is a Monday.
        var monday = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(monday, BucketAlignment.Iso8601Week, OneHour);

        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void NextBucketEnd_CalendarMonth_AddsOneCalendarMonth_NotFixed30Days()
    {
        // January→February (31 days) and February→March (28 days) both move by ONE calendar
        // month, never a fixed number of days. The calendar-month variant exists precisely to
        // express "monthly EDA rollups" without mismodelling them as 30d FixedSize buckets.
        var jan = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            BucketBoundary.NextBucketEnd(jan, BucketAlignment.CalendarMonth, OneHour));
        Assert.Equal(
            new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            BucketBoundary.NextBucketEnd(feb, BucketAlignment.CalendarMonth, OneHour));
    }

    [Fact]
    public void NextBucketEnd_CalendarYear_AddsOneYear()
    {
        var year = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(year, BucketAlignment.CalendarYear, OneHour);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    // ---- InitialWatermark ------------------------------------------------------------------

    [Fact]
    public void InitialWatermark_FixedSize_SnapsOneBucketBehind()
    {
        // now=14:23 with bucketSize=1h ⇒ watermark = (14:23 - 1h) truncated to hour = 13:00.
        // The orchestrator's first tick then processes [13:00, 14:00) as a fully-completed
        // backlog of one — same as the legacy behaviour.
        var now = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.FixedSize, OneHour);

        Assert.Equal(new DateTime(2026, 5, 11, 13, 0, 0, DateTimeKind.Utc), watermark);
    }

    [Fact]
    public void InitialWatermark_CalendarDay_AnchorsAtMidnightOfToday()
    {
        // now=2026-05-11 14:23 UTC ⇒ start of containing day = 2026-05-11 00:00. Next bucket
        // processed = [2026-05-11 00:00, 2026-05-12 00:00) — i.e. today — which won't fire until
        // tomorrow + watermarkLag, so the first tick has no backlog. That's deliberate for
        // calendar variants: the bucket the user wants is "yesterday's data", and yesterday
        // already aggregated as its own activation in prior runs. On fresh activation, the
        // operator can rewind if backfill is desired.
        var now = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.CalendarDay, OneHour);

        Assert.Equal(new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc), watermark);
    }

    [Fact]
    public void InitialWatermark_CalendarMonth_AnchorsAtFirstOfMonth()
    {
        var now = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.CalendarMonth, OneHour);

        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), watermark);
    }

    [Theory]
    [InlineData(2026, 5, 11, 2026, 5, 11)] // Monday → same Monday
    [InlineData(2026, 5, 13, 2026, 5, 11)] // Wednesday → previous Monday
    [InlineData(2026, 5, 17, 2026, 5, 11)] // Sunday → previous Monday (ISO week ends Sunday)
    [InlineData(2026, 1, 1, 2025, 12, 29)] // 2026-01-01 was a Thursday → 2025-12-29 Monday
    public void InitialWatermark_Iso8601Week_AnchorsAtMostRecentMonday(
        int year, int month, int day, int expectedYear, int expectedMonth, int expectedDay)
    {
        var now = new DateTime(year, month, day, 9, 30, 0, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.Iso8601Week, OneHour);

        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay, 0, 0, 0, DateTimeKind.Utc), watermark);
    }

    [Fact]
    public void InitialWatermark_CalendarYear_AnchorsAtJanuaryFirst()
    {
        var now = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.CalendarYear, OneHour);

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), watermark);
    }

    // ---- AlignDown -------------------------------------------------------------------------

    [Fact]
    public void AlignDown_FixedSize_TruncatesToBucketSize()
    {
        var target = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(target, BucketAlignment.FixedSize, OneHour);

        Assert.Equal(new DateTime(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc), aligned);
    }

    [Fact]
    public void AlignDown_CalendarMonth_TruncatesToFirstOfMonth()
    {
        var target = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(target, BucketAlignment.CalendarMonth, OneHour);

        Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), aligned);
    }

    // ---- Zone-aware calendar (AB#4290 / O6) ------------------------------------------------

    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    [Fact]
    public void NextBucketEnd_CalendarDay_ZoneAware_SpringForwardDayIs23Hours()
    {
        // Local midnight 2026-03-29 in Vienna (winter, UTC+1) = 2026-03-28 23:00 UTC. The DST
        // spring-forward (02:00→03:00) makes that local day 23 h long; the next local midnight
        // (summer, UTC+2) is 2026-03-29 22:00 UTC.
        var bucketStart = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(bucketStart, BucketAlignment.CalendarDay, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromHours(23), end - bucketStart);
    }

    [Fact]
    public void NextBucketEnd_CalendarDay_ZoneAware_FallBackDayIs25Hours()
    {
        // Local midnight 2026-10-25 in Vienna (summer, UTC+2) = 2026-10-24 22:00 UTC. The DST
        // fall-back (03:00→02:00) makes that local day 25 h long; the next local midnight
        // (winter, UTC+1) is 2026-10-25 23:00 UTC.
        var bucketStart = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(bucketStart, BucketAlignment.CalendarDay, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromHours(25), end - bucketStart);
    }

    [Fact]
    public void AlignDown_CalendarDay_ZoneAware_SnapsToLocalMidnight()
    {
        // 2026-03-29 12:00 Vienna (summer, UTC+2) = 2026-03-29 10:00 UTC. Local-day start is
        // 2026-03-29 00:00 Vienna (winter side of the transition, UTC+1) = 2026-03-28 23:00 UTC.
        var target = new DateTime(2026, 3, 29, 10, 0, 0, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(target, BucketAlignment.CalendarDay, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc), aligned);
    }

    [Fact]
    public void NullZone_ReproducesUtcCalendarExactly()
    {
        // The zone=null path must be byte-identical to the no-zone overload (the UTC calendar
        // behaviour the orchestrator depends on).
        var start = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);
        foreach (var alignment in new[]
                 {
                     BucketAlignment.CalendarDay, BucketAlignment.Iso8601Week,
                     BucketAlignment.CalendarMonth, BucketAlignment.CalendarYear,
                 })
        {
            Assert.Equal(
                BucketBoundary.NextBucketEnd(start, alignment, OneHour),
                BucketBoundary.NextBucketEnd(start, alignment, OneHour, zone: null));
            Assert.Equal(
                BucketBoundary.AlignDown(start, alignment, OneHour),
                BucketBoundary.AlignDown(start, alignment, OneHour, zone: null));
        }
    }
}
