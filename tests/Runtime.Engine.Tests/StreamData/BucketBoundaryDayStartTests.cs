using System;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#7358: a CalendarDay bucket that starts at a local time of day (gas day, 06:00 Europe/Vienna).
/// The offset is applied on the local wall clock, so a bucket is 23, 24 or 25 hours across DST.
/// </summary>
public class BucketBoundaryDayStartTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private static readonly TimeSpan SixAm = TimeSpan.FromHours(6);

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static BucketCalendar GasDay(TimeSpan? dayStart = null) =>
        new(BucketAlignment.CalendarDay, OneHour, Vienna, dayStart ?? SixAm);

    // ---- AC2: boundaries -----------------------------------------------------------------------

    [Fact]
    public void AlignDown_BeforeTheSpringForwardGasDay_SnapsToThePreviousSixLocal()
    {
        // 03:30Z on 29 March is 05:30 CEST: the gas day of 28 March (06:00 CET = 05:00Z) is still open.
        var aligned = BucketBoundary.AlignDown(Utc(2026, 3, 29, 3, 30), GasDay());

        Assert.Equal(Utc(2026, 3, 28, 5), aligned);
    }

    [Fact]
    public void NextBucketEnd_SpringForwardGasDay_Is23Hours()
    {
        var end = BucketBoundary.NextBucketEnd(Utc(2026, 3, 28, 5), GasDay());

        Assert.Equal(Utc(2026, 3, 29, 4), end);
        Assert.Equal(TimeSpan.FromHours(23), end - Utc(2026, 3, 28, 5));
    }

    [Fact]
    public void NextBucketEnd_FallBackGasDay_Is25Hours()
    {
        var end = BucketBoundary.NextBucketEnd(Utc(2026, 10, 24, 4), GasDay());

        Assert.Equal(Utc(2026, 10, 25, 5), end);
        Assert.Equal(TimeSpan.FromHours(25), end - Utc(2026, 10, 24, 4));
    }

    [Fact]
    public void NextBucketEnd_SummerGasDay_Is24Hours()
    {
        var end = BucketBoundary.NextBucketEnd(Utc(2026, 7, 1, 4), GasDay());

        Assert.Equal(Utc(2026, 7, 2, 4), end);
    }

    [Fact]
    public void InitialWatermark_LandsOnASixLocalBoundary()
    {
        // 12:00 local on 1 July: the last completed bucket ended at 06:00 local = 04:00Z.
        var summer = BucketBoundary.InitialWatermark(Utc(2026, 7, 1, 10), GasDay());
        // 04:00 local in winter is before the day start: the last completed bucket ended 06:00 local the day before.
        var winter = BucketBoundary.InitialWatermark(Utc(2026, 1, 15, 3), GasDay());

        Assert.Equal(Utc(2026, 7, 1, 4), summer);
        Assert.Equal(Utc(2026, 1, 14, 5), winter);
    }

    [Fact]
    public void AlignDown_IsIdempotent_AndExactlyOnTheBoundaryStaysPut()
    {
        var start = Utc(2026, 7, 1, 4);

        Assert.Equal(start, BucketBoundary.AlignDown(start, GasDay()));
        Assert.Equal(start, BucketBoundary.AlignDown(BucketBoundary.AlignDown(Utc(2026, 7, 1, 20), GasDay()), GasDay()));
        Assert.Equal(Utc(2026, 6, 30, 4), BucketBoundary.AlignDown(start - TimeSpan.FromTicks(1), GasDay()));
    }

    [Fact]
    public void ConsecutiveBuckets_AcrossBothDstChanges_StayOnSixLocal()
    {
        var cursor = Utc(2026, 3, 27, 5); // 06:00 CET
        for (var i = 0; i < 220; i++)
        {
            cursor = BucketBoundary.NextBucketEnd(cursor, GasDay());
            var local = TimeZoneInfo.ConvertTimeFromUtc(cursor, Vienna);
            Assert.Equal(SixAm, local.TimeOfDay);
        }
    }

    [Fact]
    public void ADayStartInsideTheSpringForwardGap_ShiftsThatDayOnly()
    {
        // 02:30 does not exist on 29 March in Vienna: that day starts when the clock jumps (03:30 CEST).
        var gapDay = GasDay(TimeSpan.FromHours(2.5));
        var start = BucketBoundary.AlignDown(Utc(2026, 3, 29, 1, 45), gapDay); // 03:45 CEST

        Assert.Equal(Utc(2026, 3, 29, 1, 30), start);
        // The following day starts at 02:30 again, not at the shifted 03:30.
        Assert.Equal(Utc(2026, 3, 30, 0, 30), BucketBoundary.NextBucketEnd(start, gapDay));
    }


    [Fact]
    public void AlignDown_InputBeforeAShiftedGapDayStart_BelongsToThePreviousDay()
    {
        // 03:10 CEST (01:10Z) on 29 March: the 02:30 day start of that day was shifted to 03:30 CEST (01:30Z),
        // which is after the input, so AlignDown must not return a time later than its input.
        var gapDay = GasDay(TimeSpan.FromHours(2.5));
        var input = Utc(2026, 3, 29, 1, 10);

        var aligned = BucketBoundary.AlignDown(input, gapDay);

        Assert.Equal(Utc(2026, 3, 28, 1, 30), aligned);
        Assert.True(aligned <= input);
        Assert.Equal(Utc(2026, 3, 29, 1, 30), BucketBoundary.NextBucketEnd(aligned, gapDay));
    }

    [Fact]
    public void ADayStartInsideATwoHourGap_DoesNotThrow_AndStartsTheGapLengthLater()
    {
        // Antarctica/Troll skips 01:00-03:00 on 29 March 2026 (UTC+0 to UTC+2). A 01:30 day start does not exist
        // that day; it starts at 03:30 local (01:30Z). Platforms without the zone data skip the test.
        TimeZoneInfo troll;
        try { troll = TimeZoneInfo.FindSystemTimeZoneById("Antarctica/Troll"); }
        catch (TimeZoneNotFoundException) { return; }
        var calendar = new BucketCalendar(BucketAlignment.CalendarDay, OneHour, troll, TimeSpan.FromHours(1.5));

        var start = BucketBoundary.AlignDown(Utc(2026, 3, 29, 2), calendar);
        var next = BucketBoundary.NextBucketEnd(Utc(2026, 3, 28, 1, 30), calendar);

        Assert.Equal(Utc(2026, 3, 29, 1, 30), next);
        Assert.Equal(next, start);
    }
    [Fact]
    public void WithoutAZone_TheDayStartAppliesToUtc()
    {
        var calendar = new BucketCalendar(BucketAlignment.CalendarDay, OneHour, null, SixAm);

        Assert.Equal(Utc(2026, 6, 30, 6), BucketBoundary.AlignDown(Utc(2026, 7, 1, 3), calendar));
        Assert.Equal(Utc(2026, 7, 2, 6), BucketBoundary.NextBucketEnd(Utc(2026, 7, 1, 6), calendar));
    }

    // ---- AC3: no regression --------------------------------------------------------------------

    [Theory]
    [InlineData(BucketAlignment.CalendarDay)]
    [InlineData(BucketAlignment.Iso8601Week)]
    [InlineData(BucketAlignment.CalendarMonth)]
    [InlineData(BucketAlignment.CalendarQuarter)]
    [InlineData(BucketAlignment.CalendarYear)]
    [InlineData(BucketAlignment.FixedSize)]
    public void NoDayStart_IsIdenticalToTheLegacyOverloads(BucketAlignment alignment)
    {
        var t = Utc(2026, 3, 29, 3, 30);
        foreach (var zone in new TimeZoneInfo?[] { null, Vienna })
        {
            var calendar = new BucketCalendar(alignment, OneHour, zone);
            Assert.Equal(BucketBoundary.AlignDown(t, alignment, OneHour, zone), BucketBoundary.AlignDown(t, calendar));
            Assert.Equal(BucketBoundary.NextBucketEnd(t, alignment, OneHour, zone), BucketBoundary.NextBucketEnd(t, calendar));
            Assert.Equal(BucketBoundary.InitialWatermark(t, alignment, OneHour, zone), BucketBoundary.InitialWatermark(t, calendar));
        }
    }

    [Fact]
    public void ZeroDayStart_EqualsMidnightDays()
    {
        var midnight = new BucketCalendar(BucketAlignment.CalendarDay, OneHour, Vienna);
        var zero = new BucketCalendar(BucketAlignment.CalendarDay, OneHour, Vienna, TimeSpan.Zero);
        var t = Utc(2026, 3, 29, 3, 30);

        Assert.Equal(BucketBoundary.AlignDown(t, midnight), BucketBoundary.AlignDown(t, zero));
        Assert.Equal(Utc(2026, 3, 28, 23), BucketBoundary.AlignDown(t, zero));
    }

    [Fact]
    public void ADayStartOnANonDayAlignment_IsIgnored()
    {
        var month = new BucketCalendar(BucketAlignment.CalendarMonth, OneHour, Vienna, SixAm);

        Assert.Equal(Utc(2026, 2, 28, 23), BucketBoundary.AlignDown(Utc(2026, 3, 15), month));
    }

    // ---- BucketCalendar factories --------------------------------------------------------------

    [Theory]
    [InlineData(null, 0L)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, 0L)]
    [InlineData(21_600_000L, 21_600_000L)]
    public void DayStartFromMs_NullZeroAndNegativeMeanMidnight(long? ms, long expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), BucketCalendar.DayStartFromMs(ms));

    [Fact]
    public void For_Snapshot_And_Rung_CarryTheDayStartAndTheZone()
    {
        var rollup = new RollupArchiveSnapshot(
            OctoObjectId.GenerateNewId(), new RtCkId<CkTypeId>("Test", new CkTypeId("CkRollupArchive")),
            CkArchiveStatus.Activated, null,
            Array.Empty<RollupSourceReference>(), TimeSpan.FromDays(1), TimeSpan.Zero, null,
            Array.Empty<CkRollupAggregationSpec>(), null)
        {
            BucketAlignment = BucketAlignment.CalendarDay,
            ReferenceTimeZone = "Europe/Vienna",
            DayStartOffsetMs = 21_600_000,
        };
        var rung = new ResolutionRung(
            rollup.RtId, 86_400_000, BucketAlignment.CalendarDay, Array.Empty<CkRollupFunction>(), IsBase: false)
        {
            ReferenceTimeZone = "Europe/Vienna",
            DayStartOffsetMs = 21_600_000,
        };

        var fromRollup = BucketCalendar.For(rollup);
        var fromRung = BucketCalendar.For(rung);

        Assert.Equal(SixAm, fromRollup.DayStart);
        Assert.Equal(SixAm, fromRung.DayStart);
        Assert.NotNull(fromRollup.Zone);
        Assert.True(fromRollup.HasDayStart);
        Assert.Equal(Utc(2026, 3, 28, 5), BucketBoundary.AlignDown(Utc(2026, 3, 29, 3, 30), fromRollup));
    }
}
