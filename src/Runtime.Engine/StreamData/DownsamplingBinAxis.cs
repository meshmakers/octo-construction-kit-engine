using System;
using System.Collections.Generic;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Validates the local-day bin options of a downsampling query and builds its bin axis (AB#7360). The axis is
/// computed with <see cref="BucketBoundary"/> on a <see cref="BucketCalendar"/>, the same primitive that cuts the
/// buckets of day rollups, so bins of a raw query and buckets of a rollup (e.g. gas days, 06:00 Europe/Vienna)
/// coincide on the instant, also on the 23 h and 25 h days.
/// </summary>
public static class DownsamplingBinAxis
{
    /// <summary>The most bins one query may ask for.</summary>
    public const int MaxBins = 10_000;

    /// <summary>
    /// Checks the bin options; throws <see cref="DownsamplingBinOptionsInvalidException"/> with a message that
    /// names the offending option. Returns the resolved calendar (day alignment, zone, origin) and the interval in
    /// days. Does nothing for options without local-day bins (<see cref="StreamDataDownsamplingQueryOptions.HasLocalDayBins"/>
    /// false): the caller must not call it then.
    /// </summary>
    public static (BucketCalendar Calendar, int IntervalDays) Resolve(StreamDataDownsamplingQueryOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        TimeZoneInfo? zone = null;
        if (options.TimeZone is { } zoneId)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new DownsamplingBinOptionsInvalidException(
                    $"TimeZone '{zoneId}' is not a known time zone. Use an IANA id such as 'Europe/Vienna'.");
            }
        }

        var origin = options.BinOrigin ?? TimeSpan.Zero;
        if (origin < TimeSpan.Zero || origin >= TimeSpan.FromDays(1))
        {
            throw new DownsamplingBinOptionsInvalidException(
                $"BinOrigin {origin} is outside 00:00 to 23:59. It is the local time of day at which a bin starts.");
        }

        if (origin.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new DownsamplingBinOptionsInvalidException($"BinOrigin {origin} is not a whole minute.");
        }

        var interval = options.BinInterval ?? TimeSpan.FromDays(1);
        if (interval <= TimeSpan.Zero || interval.Ticks % TimeSpan.TicksPerDay != 0)
        {
            throw new DownsamplingBinOptionsInvalidException(
                $"BinInterval {interval} is not a whole number of days. With a time zone, an origin or an interval the bins are local days; use calendar rollups for weeks and months.");
        }

        var days = interval.Ticks / TimeSpan.TicksPerDay;
        if (days > MaxBins)
        {
            throw new DownsamplingBinOptionsInvalidException($"BinInterval {interval} is longer than {MaxBins} days.");
        }

        return (new BucketCalendar(BucketAlignment.CalendarDay, TimeSpan.FromDays(1), zone, origin), (int)days);
    }

    /// <summary>
    /// The bin starts (UTC) of a local-day query over <c>[from, to)</c>, empty bins included: the first is the day
    /// start that contains <paramref name="from"/>, then every <paramref name="intervalDays"/> local days while the
    /// start lies before <paramref name="to"/>. Throws <see cref="DownsamplingBinOptionsInvalidException"/> when the
    /// axis would exceed <see cref="MaxBins"/>.
    /// </summary>
    public static IReadOnlyList<DateTime> Build(DateTime from, DateTime to, BucketCalendar calendar, int intervalDays)
    {
        var axis = new List<DateTime>();
        if (to <= from)
        {
            return axis;
        }

        var cursor = BucketBoundary.AlignDown(from, calendar);
        while (cursor < to)
        {
            if (axis.Count >= MaxBins)
            {
                throw new DownsamplingBinOptionsInvalidException(
                    $"the time range needs more than {MaxBins} bins. Ask for a shorter range or a longer BinInterval.");
            }

            axis.Add(cursor);
            for (var i = 0; i < intervalDays; i++)
            {
                cursor = BucketBoundary.NextBucketEnd(cursor, calendar);
            }
        }

        return axis;
    }

    /// <summary>Validates the options and builds the axis for their time range.</summary>
    public static IReadOnlyList<DateTime> Build(StreamDataDownsamplingQueryOptions options)
    {
        var (calendar, days) = Resolve(options);
        if (options.From is not { } from || options.To is not { } to)
        {
            throw new DownsamplingBinOptionsInvalidException("a time range (From and To) is required for local-day bins.");
        }

        return Build(from, to, calendar, days);
    }
}
