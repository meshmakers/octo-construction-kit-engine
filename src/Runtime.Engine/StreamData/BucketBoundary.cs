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
/// </remarks>
public static class BucketBoundary
{
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
    /// boundary (next day / Monday / first-of-month / Jan 1), in <paramref name="zone"/> if supplied.
    /// </summary>
    public static DateTime NextBucketEnd(
        DateTime bucketStart, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null)
    {
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
                BucketAlignment.CalendarYear => utc.AddYears(1),
                _ => throw UnknownAlignment(alignment)
            };
        }

        // Zone-aware: advance one local calendar unit from the local boundary, then back to UTC.
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var nextLocal = alignment switch
        {
            BucketAlignment.CalendarDay => local.AddDays(1),
            BucketAlignment.Iso8601Week => local.AddDays(7),
            BucketAlignment.CalendarMonth => local.AddMonths(1),
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
        DateTime now, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null)
    {
        var utc = ToUtc(now);
        return alignment == BucketAlignment.FixedSize
            ? AlignDownFixed(utc - bucketSize, bucketSize)
            : AlignDownCalendar(utc, alignment, zone);
    }

    /// <summary>
    /// Snaps <paramref name="target"/> down to the start of the period containing it. Used by
    /// <c>RewindWatermarkAsync</c> so a rewind target lands on a valid bucket boundary regardless
    /// of which arbitrary timestamp the caller passes, and by the resolution-aware grain probe.
    /// </summary>
    public static DateTime AlignDown(
        DateTime target, BucketAlignment alignment, TimeSpan bucketSize, TimeZoneInfo? zone = null)
    {
        var utc = ToUtc(target);
        return alignment == BucketAlignment.FixedSize
            ? (bucketSize.Ticks > 0 ? AlignDownFixed(utc, bucketSize) : utc)
            : AlignDownCalendar(utc, alignment, zone);
    }

    private static DateTime AlignDownCalendar(DateTime utc, BucketAlignment alignment, TimeZoneInfo? zone)
    {
        if (zone is null)
        {
            return alignment switch
            {
                BucketAlignment.CalendarDay => AlignDownToDay(utc),
                BucketAlignment.Iso8601Week => AlignDownToIso8601Week(utc),
                BucketAlignment.CalendarMonth => AlignDownToMonth(utc),
                BucketAlignment.CalendarYear => AlignDownToYear(utc),
                _ => throw UnknownAlignment(alignment)
            };
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var alignedLocal = alignment switch
        {
            BucketAlignment.CalendarDay => AlignDownToDay(local),
            BucketAlignment.Iso8601Week => AlignDownToIso8601Week(local),
            BucketAlignment.CalendarMonth => AlignDownToMonth(local),
            BucketAlignment.CalendarYear => AlignDownToYear(local),
            _ => throw UnknownAlignment(alignment)
        };
        return ToUtcFromLocal(alignedLocal, zone);
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

    private static DateTime AlignDownToYear(DateTime t) =>
        new(t.Year, 1, 1, 0, 0, 0, t.Kind);

    private static DateTime ToUtcFromLocal(DateTime local, TimeZoneInfo zone)
    {
        // The aligners preserve Kind; normalise to Unspecified so ConvertTimeToUtc treats the value
        // as wall-clock time in `zone` rather than machine-local.
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
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
