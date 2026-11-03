using System;
using System.Linq;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>AB#7360: validation and bin axis of a downsampling query with local-day bins.</summary>
public class DownsamplingBinAxisTests
{
    private static readonly TimeSpan SixAm = TimeSpan.FromHours(6);

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    private static StreamDataDownsamplingQueryOptions Options(
        string? zone = "Europe/Vienna", TimeSpan? origin = null, TimeSpan? interval = null,
        DateTime? from = null, DateTime? to = null) =>
        StreamDataDownsamplingQueryOptions.Create()
            .WithTimeZone(zone)
            .WithBinOrigin(origin)
            .WithBinInterval(interval)
            .WithTimeRange(from ?? Utc(2026, 3, 27, 5), to ?? Utc(2026, 3, 30, 4));

    [Fact]
    public void WithoutZoneOriginAndInterval_NoLocalDayBins()
    {
        var options = StreamDataDownsamplingQueryOptions.Create();

        Assert.False(options.HasLocalDayBins);
        Assert.Null(options.TimeZone);
        Assert.Null(options.BinInterval);
        Assert.Null(options.BinOrigin);
    }

    [Theory]
    [InlineData("Europe/Vienna", null, null)]
    [InlineData(null, 6, null)]
    [InlineData(null, null, 1)]
    public void AnyOfTheThreeOptions_SwitchesToLocalDayBins(string? zone, int? originHours, int? intervalDays)
    {
        var options = StreamDataDownsamplingQueryOptions.Create()
            .WithTimeZone(zone)
            .WithBinOrigin(originHours is { } h ? TimeSpan.FromHours(h) : null)
            .WithBinInterval(intervalDays is { } d ? TimeSpan.FromDays(d) : null);

        Assert.True(options.HasLocalDayBins);
    }

    [Fact]
    public void GasDays_SpringForward_ThreeBinsOf24h23h24h()
    {
        // AC1: 06:00 Vienna, one day, 2026-03-27T05:00Z to 2026-03-30T04:00Z.
        var axis = DownsamplingBinAxis.Build(Options(origin: SixAm, interval: TimeSpan.FromDays(1)));

        Assert.Equal(new[] { Utc(2026, 3, 27, 5), Utc(2026, 3, 28, 5), Utc(2026, 3, 29, 4) }, axis);
        Assert.Equal(TimeSpan.FromHours(23), axis[2] - axis[1]);
    }

    [Fact]
    public void GasDays_FallBack_TheGasDayOf24OctoberHas25Hours()
    {
        var axis = DownsamplingBinAxis.Build(Options(origin: SixAm, from: Utc(2026, 10, 24, 4), to: Utc(2026, 10, 26, 5)));

        Assert.Equal(new[] { Utc(2026, 10, 24, 4), Utc(2026, 10, 25, 5) }, axis);
        Assert.Equal(TimeSpan.FromHours(25), axis[1] - axis[0]);
    }

    [Fact]
    public void MidnightDays_InVienna_AreTheLocalCalendarDays()
    {
        // 2026-03-29 has 23 hours; the axis starts at the day that contains From.
        var axis = DownsamplingBinAxis.Build(Options(from: Utc(2026, 3, 27, 12), to: Utc(2026, 3, 30, 22)));

        Assert.Equal(new[] { Utc(2026, 3, 26, 23), Utc(2026, 3, 27, 23), Utc(2026, 3, 28, 23), Utc(2026, 3, 29, 22) }, axis);
    }

    [Fact]
    public void FirstBin_IsTheDayStartThatContainsFrom_AlsoWhenFromIsBeforeTheOrigin()
    {
        // 03:30Z on 29 March is 05:30 CEST: the gas day of 28 March is still open.
        var axis = DownsamplingBinAxis.Build(Options(origin: SixAm, from: Utc(2026, 3, 29, 3, 30), to: Utc(2026, 3, 29, 5)));

        Assert.Equal(new[] { Utc(2026, 3, 28, 5), Utc(2026, 3, 29, 4) }, axis);
    }

    [Fact]
    public void IntervalOfSeveralDays_StepsInLocalDays()
    {
        var axis = DownsamplingBinAxis.Build(Options(
            origin: SixAm, interval: TimeSpan.FromDays(2), from: Utc(2026, 3, 27, 5), to: Utc(2026, 4, 2, 4)));

        Assert.Equal(new[] { Utc(2026, 3, 27, 5), Utc(2026, 3, 29, 4), Utc(2026, 3, 31, 4) }, axis);
    }

    [Fact]
    public void OnlyAnInterval_CutsUtcDays()
    {
        var axis = DownsamplingBinAxis.Build(Options(
            zone: null, interval: TimeSpan.FromDays(1), from: Utc(2026, 3, 27, 5), to: Utc(2026, 3, 29, 1)));

        Assert.Equal(new[] { Utc(2026, 3, 27), Utc(2026, 3, 28), Utc(2026, 3, 29) }, axis);
    }

    [Fact]
    public void EmptyRange_GivesAnEmptyAxis() =>
        Assert.Empty(DownsamplingBinAxis.Build(Options(from: Utc(2026, 3, 27), to: Utc(2026, 3, 27))));

    [Fact]
    public void BinStarts_AreOnTheSameInstantsAsTheBucketsOfADayRollup()
    {
        // The rollup side: BucketBoundary on the same calendar (zone and day start).
        var calendar = new BucketCalendar(BucketAlignment.CalendarDay, TimeSpan.FromDays(1),
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"), SixAm);
        var cursor = BucketBoundary.AlignDown(Utc(2026, 1, 1), calendar);
        var expected = Enumerable.Range(0, 366).Select(_ =>
        {
            var current = cursor;
            cursor = BucketBoundary.NextBucketEnd(cursor, calendar);
            return current;
        }).ToArray();

        var axis = DownsamplingBinAxis.Build(Options(origin: SixAm, from: Utc(2026, 1, 1), to: cursor));

        Assert.Equal(expected, axis);
    }

    // ---- validation (AC5) ------------------------------------------------------------------------

    [Fact]
    public void UnknownZone_IsRejectedAndNamed()
    {
        var ex = Assert.Throws<DownsamplingBinOptionsInvalidException>(
            () => DownsamplingBinAxis.Resolve(Options(zone: "Mars/Olympus")));

        Assert.Contains("TimeZone", ex.Message);
        Assert.Contains("Mars/Olympus", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24 * 60)]
    [InlineData(25 * 60)]
    public void OriginOutsideTheDay_IsRejected(int minutes)
    {
        var ex = Assert.Throws<DownsamplingBinOptionsInvalidException>(
            () => DownsamplingBinAxis.Resolve(Options(origin: TimeSpan.FromMinutes(minutes))));

        Assert.Contains("BinOrigin", ex.Message);
    }

    [Fact]
    public void OriginNotOnAWholeMinute_IsRejected()
    {
        var ex = Assert.Throws<DownsamplingBinOptionsInvalidException>(
            () => DownsamplingBinAxis.Resolve(Options(origin: TimeSpan.FromSeconds(6 * 3600 + 30))));

        Assert.Contains("whole minute", ex.Message);
    }

    [Fact]
    public void LastMinuteOfTheDay_IsAValidOrigin() =>
        DownsamplingBinAxis.Resolve(Options(origin: new TimeSpan(23, 59, 0)));

    [Theory]
    [InlineData(36)]
    [InlineData(0)]
    [InlineData(-24)]
    public void IntervalNotAWholeNumberOfDays_IsRejected(int hours)
    {
        var ex = Assert.Throws<DownsamplingBinOptionsInvalidException>(
            () => DownsamplingBinAxis.Resolve(Options(interval: TimeSpan.FromHours(hours))));

        Assert.Contains("BinInterval", ex.Message);
        Assert.Contains("whole number of days", ex.Message);
    }

    [Fact]
    public void MoreBinsThanTheMaximum_IsRejected()
    {
        var ex = Assert.Throws<DownsamplingBinOptionsInvalidException>(() => DownsamplingBinAxis.Build(
            Options(from: Utc(1990, 1, 1), to: Utc(2026, 1, 1))));

        Assert.Contains(DownsamplingBinAxis.MaxBins.ToString(), ex.Message);
    }

    [Fact]
    public void ExactlyTheMaximum_IsAccepted()
    {
        var from = Utc(2000, 1, 1);

        var axis = DownsamplingBinAxis.Build(Options(zone: null, from: from, to: from.AddDays(DownsamplingBinAxis.MaxBins)));

        Assert.Equal(DownsamplingBinAxis.MaxBins, axis.Count);
    }

    [Fact]
    public void MissingTimeRange_IsRejected() =>
        Assert.Throws<DownsamplingBinOptionsInvalidException>(() => DownsamplingBinAxis.Build(
            StreamDataDownsamplingQueryOptions.Create().WithTimeZone("Europe/Vienna")));
}
