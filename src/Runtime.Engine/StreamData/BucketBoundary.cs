using System;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Pure-function bucket-boundary arithmetic for a <c>RollupArchive</c> — computes the next bucket
/// end from the current one, and the initial watermark when a rollup is activated. Branches on
/// <see cref="BucketAlignment"/> so calendar/ISO-week variants (used by monthly / weekly EDA
/// rollups) get the right boundary instead of the strict <c>x + BucketSize</c> arithmetic that
/// FixedSize uses. Concept-time-range §7.
/// </summary>
/// <remarks>
/// All operations return UTC <see cref="DateTime"/> values. When <c>zone</c> is <c>null</c> the
/// calendar variants align to UTC calendar boundaries — the legacy behaviour, preserved
/// byte-for-byte. When a reference <see cref="TimeZoneInfo"/> is supplied (AB#4290 / O6), calendar
/// boundaries snap to <em>local</em> wall-clock day / week / month / year boundaries and are
/// DST-correct (a local calendar day across a DST transition is 23 h or 25 h, not a fixed 24 h).
/// <c>BucketSize</c> is only consulted for <see cref="BucketAlignment.FixedSize"/>; the calendar
/// variants ignore it entirely (the attribute is kept informational for monitoring / UI).
/// <see cref="BucketAlignment.CalendarQuarter"/> (AB#5157) follows the CalendarMonth shape exactly:
/// quarters start on 1 January / April / July / October at local midnight, so the quarter that
/// contains the spring-forward transition (Q1 in Europe/Vienna) is 90 d minus 1 h and the one that
/// contains the fall-back transition (Q4) is 92 d plus 1 h in UTC; Q2 and Q3 are exactly 91 d and
/// 92 d.
/// </remarks>
public static class BucketBoundary
{
    /// <summary>
    /// The distinct UTC offsets a zone has used since 1970 (standard and daylight time); <c>{ 0 }</c> for no zone.
    /// Earlier rules (local mean time with odd seconds) are ignored: no archive holds data from then.
    /// </summary>
    /// <remarks>
    /// The cut-off is deliberately 1970 and not a later year (AB#7360): an archive may hold data of any time since
    /// then, and a source whose buckets do not divide an offset the zone once used would cut local days in two for
    /// that data. The consequence is that a zone whose offset changed later (Europe/Moscow, Europe/Istanbul) is
    /// judged against its former offsets as well, so a fixed-size source may be refused that divides only the
    /// current ones. The offsets come from the host's time zone data; two hosts with different tzdata can answer
    /// differently for such zones. Vienna, Berlin, London and the other zones with whole-hour offsets are unaffected.
    /// </remarks>
    public static IReadOnlyCollection<TimeSpan> UtcOffsetsOf(TimeZoneInfo? zone)
    {
        var offsets = new HashSet<TimeSpan>();
        if (zone is null)
        {
            offsets.Add(TimeSpan.Zero);
            return offsets;
        }

        offsets.Add(zone.BaseUtcOffset);
        var epoch = new DateTime(1970, 1, 1);
        foreach (var rule in zone.GetAdjustmentRules())
        {
            if (rule.DateEnd < epoch)
            {
                continue;
            }

            var standard = zone.BaseUtcOffset + rule.BaseUtcOffsetDelta;
            offsets.Add(standard);
            offsets.Add(standard + rule.DaylightDelta);
        }

        return offsets;
    }

    /// <summary>
    /// Resolves an IANA reference-time-zone id (AB#4290 / O6) to a <see cref="TimeZoneInfo"/>, or
    /// <c>null</c> (⇒ UTC calendar boundaries) when the id is empty or unknown. Tolerant by design:
    /// this is the write/aggregation path, where an unknown stored id must fall back to UTC rather
    /// than crash a background orchestrator. Create-time input is validated fail-fast elsewhere
    /// (<c>RollupArchiveLifecycleService.CreateAsync</c>). Single source of truth for zone resolution,
    /// shared by the engine write path, the CrateDb recompute executor, and the read-side resolver.
    /// </summary>
    public static TimeZoneInfo? ResolveZone(string? referenceTimeZone)
    {
        if (string.IsNullOrWhiteSpace(referenceTimeZone))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(referenceTimeZone);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Computes the exclusive end of the bucket that starts at <paramref name="bucketStart"/>.
    /// For <see cref="BucketAlignment.FixedSize"/> this is the legacy
    /// <c>bucketStart + bucketSize</c> arithmetic; for calendar variants it's the next calendar
    /// boundary (next day / Monday / first-of-month / first-of-quarter / Jan 1), in
    /// <paramref name="zone"/> if supplied.
    /// </summary>
    public static DateTime NextBucketEnd(
        DateTime bucketStart, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null) =>
        NextBucketEnd(bucketStart, new BucketCalendar(alignment, bucketSize, zone));

    /// <inheritdoc cref="NextBucketEnd(DateTime, BucketAlignment, TimeSpan, TimeZoneInfo?)"/>
    /// <remarks>
    /// With a <see cref="BucketCalendar.DayStart"/> the day bucket runs from the local day start to the
    /// next local day start (23 / 24 / 25 h across DST changes); the offset is applied on the local
    /// wall clock, never as a UTC shift: a start on a day start advances one local day, which keeps the day start.
    /// </remarks>
    public static DateTime NextBucketEnd(DateTime bucketStart, BucketCalendar calendar)
    {
        var (alignment, bucketSize, zone, _) = calendar;
        var dayStart = calendar.HasDayStart ? calendar.DayStart : TimeSpan.Zero;
        if (alignment == BucketAlignment.FixedSize)
        {
            return bucketStart + bucketSize;
        }

        var utc = ToUtc(bucketStart);
        if (zone is null)
        {
            return alignment switch
            {
                BucketAlignment.CalendarDay => utc.AddDays(1),
                BucketAlignment.Iso8601Week => utc.AddDays(7),
                BucketAlignment.CalendarMonth => utc.AddMonths(1),
                BucketAlignment.CalendarQuarter => utc.AddMonths(3),
                BucketAlignment.CalendarYear => utc.AddYears(1),
                _ => throw UnknownAlignment(alignment)
            };
        }

        // Zone-aware: advance one local calendar unit from the local boundary, then back to UTC.
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var nextLocal = alignment switch
        {
            // Re-derive the local day from (local - dayStart): a day start that fell into a spring-forward gap was
            // shifted by ToUtcFromLocal, and plain AddDays(1) would carry that shift into every later day.
            BucketAlignment.CalendarDay when dayStart > TimeSpan.Zero =>
                AlignDownToDay(local - dayStart).AddDays(1) + dayStart,
            BucketAlignment.CalendarDay => local.AddDays(1),
            BucketAlignment.Iso8601Week => local.AddDays(7),
            BucketAlignment.CalendarMonth => local.AddMonths(1),
            BucketAlignment.CalendarQuarter => local.AddMonths(3),
            BucketAlignment.CalendarYear => local.AddYears(1),
            _ => throw UnknownAlignment(alignment)
        };
        return ToUtcFromLocal(nextLocal, zone);
    }

    /// <summary>
    /// Computes the initial value for <c>LastAggregatedBucketEnd</c> when a rollup activates.
    /// Result is the exclusive end of the last fully-completed bucket at <paramref name="now"/>
    /// — so the orchestrator's next tick processes that bucket as a backlog of one.
    /// </summary>
    public static DateTime InitialWatermark(
        DateTime now, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null) =>
        InitialWatermark(now, new BucketCalendar(alignment, bucketSize, zone));

    /// <inheritdoc cref="InitialWatermark(DateTime, BucketAlignment, TimeSpan, TimeZoneInfo?)"/>
    public static DateTime InitialWatermark(DateTime now, BucketCalendar calendar)
    {
        var utc = ToUtc(now);
        return calendar.Alignment == BucketAlignment.FixedSize
            ? AlignDownFixed(utc - calendar.BucketSize, calendar.BucketSize)
            : AlignDownCalendar(utc, calendar);
    }

    /// <summary>
    /// Snaps <paramref name="target"/> down to the start of the period containing it. Used by
    /// <c>RewindWatermarkAsync</c> so a rewind target lands on a valid bucket boundary regardless
    /// of which arbitrary timestamp the caller passes, and by the resolution-aware grain probe.
    /// </summary>
    public static DateTime AlignDown(
        DateTime target, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null) =>
        AlignDown(target, new BucketCalendar(alignment, bucketSize, zone));

    /// <inheritdoc cref="AlignDown(DateTime, BucketAlignment, TimeSpan, TimeZoneInfo?)"/>
    public static DateTime AlignDown(DateTime target, BucketCalendar calendar)
    {
        var utc = ToUtc(target);
        return calendar.Alignment == BucketAlignment.FixedSize
            ? (calendar.BucketSize.Ticks > 0 ? AlignDownFixed(utc, calendar.BucketSize) : utc)
            : AlignDownCalendar(utc, calendar);
    }

    private static DateTime AlignDownCalendar(DateTime utc, BucketCalendar calendar)
    {
        var (alignment, _, zone, _) = calendar;
        var dayStart = calendar.HasDayStart ? calendar.DayStart : TimeSpan.Zero;
        if (zone is null)
        {
            return alignment switch
            {
                BucketAlignment.CalendarDay => AlignDownToDay(utc - dayStart) + dayStart,
                BucketAlignment.Iso8601Week => AlignDownToIso8601Week(utc),
                BucketAlignment.CalendarMonth => AlignDownToMonth(utc),
                BucketAlignment.CalendarQuarter => AlignDownToQuarter(utc),
                BucketAlignment.CalendarYear => AlignDownToYear(utc),
                _ => throw UnknownAlignment(alignment)
            };
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var alignedLocal = alignment switch
        {
            BucketAlignment.CalendarDay => AlignDownToDay(local - dayStart) + dayStart,
            BucketAlignment.Iso8601Week => AlignDownToIso8601Week(local),
            BucketAlignment.CalendarMonth => AlignDownToMonth(local),
            BucketAlignment.CalendarQuarter => AlignDownToQuarter(local),
            BucketAlignment.CalendarYear => AlignDownToYear(local),
            _ => throw UnknownAlignment(alignment)
        };
        var result = ToUtcFromLocal(alignedLocal, zone);
        if (dayStart > TimeSpan.Zero && alignment == BucketAlignment.CalendarDay && result > utc)
        {
            // The day start fell into a spring-forward gap and was shifted past the input: the input still
            // belongs to the previous day.
            result = ToUtcFromLocal(AlignDownToDay(local - dayStart - TimeSpan.FromDays(1)) + dayStart, zone);
        }

        return result;
    }

    private static DateTime AlignDownFixed(DateTime utc, TimeSpan bucketSize)
    {
        var ticks = bucketSize.Ticks;
        return new DateTime((utc.Ticks / ticks) * ticks, DateTimeKind.Utc);
    }

    private static DateTime AlignDownToDay(DateTime t) =>
        new(t.Year, t.Month, t.Day, 0, 0, 0, t.Kind);

    private static DateTime AlignDownToIso8601Week(DateTime t)
    {
        // ISO-8601 weeks start on Monday. DayOfWeek: Sun=0, Mon=1, ..., Sat=6.
        var daysSinceMonday = ((int)t.DayOfWeek + 6) % 7;
        var monday = new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, t.Kind);
        return monday.AddDays(-daysSinceMonday);
    }

    private static DateTime AlignDownToMonth(DateTime t) =>
        new(t.Year, t.Month, 1, 0, 0, 0, t.Kind);

    /// <summary>
    /// First day of the calendar quarter containing <paramref name="t"/> — month 1, 4, 7 or 10 —
    /// preserving <see cref="DateTime.Kind"/> exactly like the month aligner so the zone-aware
    /// path can hand the result to <see cref="ToUtcFromLocal"/>. AB#5157.
    /// </summary>
    private static DateTime AlignDownToQuarter(DateTime t) =>
        new(t.Year, ((t.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, t.Kind);

    private static DateTime AlignDownToYear(DateTime t) =>
        new(t.Year, 1, 1, 0, 0, 0, t.Kind);

    private static DateTime ToUtcFromLocal(DateTime local, TimeZoneInfo zone)
    {
        // The aligners preserve Kind; normalise to Unspecified so ConvertTimeToUtc treats the value
        // as wall-clock time in `zone` rather than machine-local.
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A day start inside a spring-forward gap does not exist; that one day starts the length of the gap
        // later (03:30 for a 02:30 start in a one-hour gap, 03:30 for 01:30 in a two-hour gap, 02:45 for 02:15 in
        // the 30-minute gap of Australia/Lord_Howe), and AlignDown steps back a day for an input before that
        // moment. The gap is measured in minutes, so a gap shorter than an hour is handled; the bounds keep a
        // bad zone from looping.
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified += GapLength(unspecified, zone);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    private static TimeSpan GapLength(DateTime invalidLocal, TimeZoneInfo zone)
    {
        const int maxMinutes = 24 * 60;
        var minute = new DateTime(invalidLocal.Ticks - invalidLocal.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Unspecified);
        var after = minute;
        for (var i = 0; i < maxMinutes && zone.IsInvalidTime(after); i++)
        {
            after = after.AddMinutes(1);
        }

        var before = minute;
        for (var i = 0; i < maxMinutes && zone.IsInvalidTime(before); i++)
        {
            before = before.AddMinutes(-1);
        }

        // `before` is the last valid minute ahead of the gap, `after` the first valid one behind it.
        return after - before.AddMinutes(1);
    }

    private static ArgumentOutOfRangeException UnknownAlignment(BucketAlignment alignment) =>
        new(nameof(alignment), alignment, "Unknown BucketAlignment value.");

    private static DateTime ToUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };
}
