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

    // ---- CalendarQuarter (AB#5157, W1-T5) ---------------------------------------------------

    // TC-MODEL-07: quarters start on 1 Jan / 1 Apr / 1 Jul / 1 Oct — every month snaps to its own.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 7)]
    [InlineData(8, 7)]
    [InlineData(9, 7)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(12, 10)]
    public void AlignDown_CalendarQuarter_Utc_SnapsEveryMonthToItsQuarterStart(int month, int expectedMonth)
    {
        var target = new DateTime(2026, month, 17, 14, 23, 17, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(target, BucketAlignment.CalendarQuarter, OneHour);

        Assert.Equal(new DateTime(2026, expectedMonth, 1, 0, 0, 0, DateTimeKind.Utc), aligned);
    }

    // TC-MODEL-08: the same twelve snaps in Europe/Vienna land on local quarter midnight, which is
    // 23:00 UTC of the previous day in winter and 22:00 UTC in summer.
    [Theory]
    [InlineData(1, 2025, 12, 31, 23)]
    [InlineData(2, 2025, 12, 31, 23)]
    [InlineData(3, 2025, 12, 31, 23)]
    [InlineData(4, 2026, 3, 31, 22)]
    [InlineData(5, 2026, 3, 31, 22)]
    [InlineData(6, 2026, 3, 31, 22)]
    [InlineData(7, 2026, 6, 30, 22)]
    [InlineData(8, 2026, 6, 30, 22)]
    [InlineData(9, 2026, 6, 30, 22)]
    [InlineData(10, 2026, 9, 30, 22)]
    [InlineData(11, 2026, 9, 30, 22)]
    [InlineData(12, 2026, 9, 30, 22)]
    public void AlignDown_CalendarQuarter_ZoneAware_SnapsToLocalQuarterMidnight(
        int month, int expectedYear, int expectedMonth, int expectedDay, int expectedHour)
    {
        var target = new DateTime(2026, month, 17, 14, 0, 0, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(target, BucketAlignment.CalendarQuarter, OneHour, Vienna);

        Assert.Equal(
            new DateTime(expectedYear, expectedMonth, expectedDay, expectedHour, 0, 0, DateTimeKind.Utc),
            aligned);
    }

    [Fact]
    public void AlignDown_CalendarQuarter_IsIdempotent()
    {
        var target = new DateTime(2026, 8, 17, 14, 23, 17, DateTimeKind.Utc);

        var once = BucketBoundary.AlignDown(target, BucketAlignment.CalendarQuarter, OneHour, Vienna);
        var twice = BucketBoundary.AlignDown(once, BucketAlignment.CalendarQuarter, OneHour, Vienna);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void NextBucketEnd_CalendarQuarter_AddsThreeCalendarMonths()
    {
        var q3 = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(q3, BucketAlignment.CalendarQuarter, OneHour);

        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void NextBucketEnd_CalendarQuarter_CrossesTheYearBoundary()
    {
        var q4 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(q4, BucketAlignment.CalendarQuarter, OneHour);

        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void NextBucketEnd_CalendarQuarter_ZoneAware_SpringForwardQuarterIsOneHourShort()
    {
        // Q1 2026 in Vienna contains the spring-forward transition (2026-03-29). Local
        // 2026-01-01 00:00 (UTC+1) = 2025-12-31 23:00 UTC; local 2026-04-01 00:00 (UTC+2) =
        // 2026-03-31 22:00 UTC. Jan+Feb+Mar = 90 calendar days, minus the lost hour.
        var q1 = new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(q1, BucketAlignment.CalendarQuarter, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromDays(90) - TimeSpan.FromHours(1), end - q1);
    }

    [Fact]
    public void NextBucketEnd_CalendarQuarter_ZoneAware_FallBackQuarterIsOneHourLong()
    {
        // Q4 2026 in Vienna contains the fall-back transition (2026-10-25). Local 2026-10-01 00:00
        // (UTC+2) = 2026-09-30 22:00 UTC; local 2027-01-01 00:00 (UTC+1) = 2026-12-31 23:00 UTC.
        // Oct+Nov+Dec = 92 calendar days, plus the repeated hour.
        var q4 = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(q4, BucketAlignment.CalendarQuarter, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromDays(92) + TimeSpan.FromHours(1), end - q4);
    }

    [Fact]
    public void NextBucketEnd_CalendarQuarter_ZoneAware_QuarterWithoutTransitionIsExact()
    {
        // Q2 2026 lies wholly inside summer time: 91 days, no DST correction.
        var q2 = new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc);

        var end = BucketBoundary.NextBucketEnd(q2, BucketAlignment.CalendarQuarter, OneHour, Vienna);

        Assert.Equal(new DateTime(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromDays(91), end - q2);
    }

    [Fact]
    public void InitialWatermark_CalendarQuarter_AnchorsAtTheStartOfTheContainingQuarter()
    {
        var now = new DateTime(2026, 5, 11, 14, 23, 17, DateTimeKind.Utc);

        var watermark = BucketBoundary.InitialWatermark(now, BucketAlignment.CalendarQuarter, OneHour);

        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), watermark);
    }

    [Fact]
    public void NullZone_CalendarQuarter_ReproducesUtcCalendarExactly()
    {
        var start = new DateTime(2026, 8, 17, 14, 23, 17, DateTimeKind.Utc);

        Assert.Equal(
            BucketBoundary.NextBucketEnd(start, BucketAlignment.CalendarQuarter, OneHour),
            BucketBoundary.NextBucketEnd(start, BucketAlignment.CalendarQuarter, OneHour, zone: null));
        Assert.Equal(
            BucketBoundary.AlignDown(start, BucketAlignment.CalendarQuarter, OneHour),
            BucketBoundary.AlignDown(start, BucketAlignment.CalendarQuarter, OneHour, zone: null));
    }

    // TC-X-VAL-09: a CalendarDay boundary that falls on the local DST switchover day is still
    // exactly on the daily grid — AlignDown maps it to itself.
    [Fact]
    public void AlignDown_CalendarDay_ZoneAware_LocalMidnightOfTheSwitchoverDayIsOnTheGrid()
    {
        // Local midnight 2026-03-29 Vienna (winter side, UTC+1) = 2026-03-28 23:00 UTC.
        var switchoverDayStart = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);

        var aligned = BucketBoundary.AlignDown(switchoverDayStart, BucketAlignment.CalendarDay, OneHour, Vienna);

        Assert.Equal(switchoverDayStart, aligned);
    }

    // ---- ResolveZone (AB#4300 / O6) --------------------------------------------------------------

    [Fact]
    public void ResolveZone_KnownIana_ReturnsZone()
    {
        Assert.NotNull(BucketBoundary.ResolveZone("Europe/Vienna"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Europe/Nowhere")]
    public void ResolveZone_NullEmptyOrUnknown_ReturnsNull(string? id)
    {
        // Tolerant on the write path: an unknown/empty id falls back to UTC (null) rather than throw.
        Assert.Null(BucketBoundary.ResolveZone(id));
    }
}
