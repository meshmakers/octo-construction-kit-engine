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
/// All operations work in UTC and return UTC <see cref="DateTime"/> values. Callers should pass
/// UTC inputs — the helpers normalise to UTC defensively but a non-UTC input would otherwise lead
/// to subtle day/month-boundary drift. <c>BucketSize</c> is only consulted for
/// <see cref="BucketAlignment.FixedSize"/>; the calendar variants ignore it entirely (the
/// attribute is kept informational for monitoring / UI).
/// </remarks>
internal static class BucketBoundary
{
    /// <summary>
    /// Computes the exclusive end of the bucket that starts at <paramref name="bucketStart"/>.
    /// For <see cref="BucketAlignment.FixedSize"/> this is the legacy
    /// <c>bucketStart + bucketSize</c> arithmetic; for calendar variants it's the next calendar
    /// boundary (next day / Monday / first-of-month / Jan 1).
    /// </summary>
    public static DateTime NextBucketEnd(DateTime bucketStart, BucketAlignment alignment, TimeSpan bucketSize)
    {
        var utc = ToUtc(bucketStart);
        return alignment switch
        {
            BucketAlignment.FixedSize => bucketStart + bucketSize,
            BucketAlignment.CalendarDay => utc.AddDays(1),
            BucketAlignment.Iso8601Week => utc.AddDays(7),
            BucketAlignment.CalendarMonth => utc.AddMonths(1),
            BucketAlignment.CalendarYear => utc.AddYears(1),
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown BucketAlignment value.")
        };
    }

    /// <summary>
    /// Computes the initial value for <c>LastAggregatedBucketEnd</c> when a rollup activates.
    /// Result is the exclusive end of the last fully-completed bucket at <paramref name="now"/>
    /// — so the orchestrator's next tick processes that bucket as a backlog of one. For FixedSize
    /// this matches the legacy <c>truncate(now - bucketSize, bucketSize)</c> formula; for
    /// calendar variants it returns the start of the period containing <paramref name="now"/>
    /// (e.g. first-of-this-month for CalendarMonth — that's the exclusive end of last month).
    /// </summary>
    public static DateTime InitialWatermark(DateTime now, BucketAlignment alignment, TimeSpan bucketSize)
    {
        var utc = ToUtc(now);
        return alignment switch
        {
            // Subtract one bucket, then snap down — preserves the legacy "seed one bucket behind"
            // semantic so the first tick after activation has a complete bucket to process.
            BucketAlignment.FixedSize => AlignDownFixed(utc - bucketSize, bucketSize),

            // For calendar variants the "start of period containing now" already IS the exclusive
            // end of the previous (fully-completed) period — no extra subtraction needed.
            BucketAlignment.CalendarDay => AlignDownToDay(utc),
            BucketAlignment.Iso8601Week => AlignDownToIso8601Week(utc),
            BucketAlignment.CalendarMonth => AlignDownToMonth(utc),
            BucketAlignment.CalendarYear => AlignDownToYear(utc),
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown BucketAlignment value.")
        };
    }

    /// <summary>
    /// Snaps <paramref name="target"/> down to the start of the period containing it. Used by
    /// <c>RewindWatermarkAsync</c> so a rewind target lands on a valid bucket boundary regardless
    /// of which arbitrary timestamp the caller passes.
    /// </summary>
    public static DateTime AlignDown(DateTime target, BucketAlignment alignment, TimeSpan bucketSize)
    {
        var utc = ToUtc(target);
        return alignment switch
        {
            BucketAlignment.FixedSize => bucketSize.Ticks > 0 ? AlignDownFixed(utc, bucketSize) : utc,
            BucketAlignment.CalendarDay => AlignDownToDay(utc),
            BucketAlignment.Iso8601Week => AlignDownToIso8601Week(utc),
            BucketAlignment.CalendarMonth => AlignDownToMonth(utc),
            BucketAlignment.CalendarYear => AlignDownToYear(utc),
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown BucketAlignment value.")
        };
    }

    private static DateTime AlignDownFixed(DateTime utc, TimeSpan bucketSize)
    {
        var ticks = bucketSize.Ticks;
        return new DateTime((utc.Ticks / ticks) * ticks, DateTimeKind.Utc);
    }

    private static DateTime AlignDownToDay(DateTime utc) =>
        new(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime AlignDownToIso8601Week(DateTime utc)
    {
        // ISO-8601 weeks start on Monday. DayOfWeek: Sun=0, Mon=1, ..., Sat=6.
        var daysSinceMonday = ((int)utc.DayOfWeek + 6) % 7;
        var monday = new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
        return monday.AddDays(-daysSinceMonday);
    }

    private static DateTime AlignDownToMonth(DateTime utc) =>
        new(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime AlignDownToYear(DateTime utc) =>
        new(utc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime ToUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };
}
