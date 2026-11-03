using System;
using System.Linq;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>AB#7360: low findings of the review of AB#7358 (PR 339) for the boundary arithmetic.</summary>
public class BucketBoundaryReviewLeftoverTests
{
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static BucketCalendar LordHoweCalendar(out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Lord_Howe");
        }
        catch (TimeZoneNotFoundException)
        {
            zone = null;
            return default;
        }

        return new BucketCalendar(BucketAlignment.CalendarDay, TimeSpan.FromDays(1), zone, new TimeSpan(2, 15, 0));
    }

    [Fact]
    public void ADayStartInAGapShorterThanAnHour_StartsTheGapLengthLater()
    {
        // Australia/Lord_Howe: on 4 October 2026 the clock jumps from 02:00 (UTC+10:30) to 02:30 (UTC+11:00).
        // A 02:15 day start does not exist that day; the day starts 30 minutes later, at 02:45 (not 03:15).
        var calendar = LordHoweCalendar(out var lordHowe);
        if (lordHowe is null)
        {
            return;
        }

        // 3 October 02:15 local (UTC+10:30) = 2 October 15:45Z.
        var previousDay = BucketBoundary.AlignDown(Utc(2026, 10, 3, 6), calendar);
        var gapDay = BucketBoundary.NextBucketEnd(previousDay, calendar);
        var following = BucketBoundary.NextBucketEnd(gapDay, calendar);

        Assert.Equal(Utc(2026, 10, 2, 15, 45), previousDay);
        // 02:45 local (UTC+11:00) = 3 October 15:45Z.
        Assert.Equal(Utc(2026, 10, 3, 15, 45), gapDay);
        Assert.Equal(new TimeSpan(2, 45, 0), TimeZoneInfo.ConvertTimeFromUtc(gapDay, lordHowe).TimeOfDay);
        // The day after starts at 02:15 again (UTC+11:00 = 4 October 15:15Z).
        Assert.Equal(Utc(2026, 10, 4, 15, 15), following);
        Assert.Equal(gapDay, BucketBoundary.AlignDown(gapDay, calendar));
    }

    [Fact]
    public void AnInputBeforeTheShiftedDayStartInTheShortGap_BelongsToThePreviousDay()
    {
        var calendar = LordHoweCalendar(out var lordHowe);
        if (lordHowe is null)
        {
            return;
        }

        // 02:30 local on 4 October (UTC+11:00) = 3 October 15:30Z, before the shifted day start of 15:45Z.
        var aligned = BucketBoundary.AlignDown(Utc(2026, 10, 3, 15, 30), calendar);

        Assert.Equal(Utc(2026, 10, 2, 15, 45), aligned);
    }

    [Fact]
    public void UtcOffsetsOf_NoZone_IsUtc() =>
        Assert.Equal(new[] { TimeSpan.Zero }, BucketBoundary.UtcOffsetsOf(null));

    [Fact]
    public void UtcOffsetsOf_Vienna_IsStandardAndDaylightTime()
    {
        var offsets = BucketBoundary.UtcOffsetsOf(TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"));

        Assert.Equal(new[] { TimeSpan.FromHours(1), TimeSpan.FromHours(2) }, offsets.OrderBy(o => o).ToArray());
    }

    [Fact]
    public void UtcOffsetsOf_CollectsRulesFrom1970AndIgnoresEarlierOnes()
    {
        // The documented cut-off (AB#7360): rules that ended before 1970 do not count, later ones do.
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Cutoff", TimeSpan.FromHours(1), "Test/Cutoff", "Test/Cutoff",
            "Test/Cutoff DST",
            new[]
            {
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(1960, 1, 1), new DateTime(1969, 12, 31), TimeSpan.FromHours(2),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25)),
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(1990, 1, 1), new DateTime(2020, 12, 31), TimeSpan.FromMinutes(30),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25)),
            });

        var offsets = BucketBoundary.UtcOffsetsOf(zone).OrderBy(o => o).ToArray();

        Assert.Equal(new[] { TimeSpan.FromHours(1), TimeSpan.FromMinutes(90) }, offsets);
    }
}
