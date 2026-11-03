using System;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Everything that determines where the buckets of a rollup begin and end: alignment, bucket size,
/// reference time zone and — for <see cref="BucketAlignment.CalendarDay"/> — the local time of day
/// at which a day starts (AB#7358, e.g. 06:00 Europe/Vienna for a gas day). Passed to
/// <see cref="BucketBoundary"/> as one value so no caller can compute boundaries and silently drop
/// the day start. Build it from a rollup with <see cref="For(RollupArchiveSnapshot)"/> or from a
/// resolution rung with <see cref="For(ResolutionRung)"/>.
/// </summary>
/// <param name="Alignment">Bucket-boundary alignment.</param>
/// <param name="BucketSize">Bucket width; only consulted for <see cref="BucketAlignment.FixedSize"/>.</param>
/// <param name="Zone">Reference time zone; <c>null</c> means UTC calendar boundaries.</param>
/// <param name="DayStart">
/// Local wall-clock offset from midnight at which a calendar day starts. <see cref="TimeSpan.Zero"/>
/// is a midnight day. Only consulted for <see cref="BucketAlignment.CalendarDay"/>.
/// </param>
public readonly record struct BucketCalendar(
    BucketAlignment Alignment,
    TimeSpan BucketSize,
    TimeZoneInfo? Zone = null,
    TimeSpan DayStart = default)
{
    /// <summary>
    /// Converts the stored <c>DayStartOffsetMs</c> (null or 0 means midnight) to a <see cref="TimeSpan"/>. A value
    /// outside <c>[0, 24 h)</c> is refused (AB#7360): save and activation validate it, but an entity updated
    /// directly could still carry one, and a negative value must not quietly become midnight nor a value of 24 h or
    /// more cut days of another length.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or at least 24 hours.</exception>
    public static TimeSpan DayStartFromMs(long? dayStartOffsetMs)
    {
        const long dayMs = 86_400_000;
        if (dayStartOffsetMs is not { } ms || ms == 0)
        {
            return TimeSpan.Zero;
        }

        if (ms < 0 || ms >= dayMs)
        {
            throw new ArgumentOutOfRangeException(nameof(dayStartOffsetMs), ms,
                $"DayStartOffsetMs must be in [0, {dayMs}) ms (a local time of day); 0 or unset means midnight.");
        }

        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>The bucket calendar of a rollup, including its reference time zone and day start.</summary>
    public static BucketCalendar For(RollupArchiveSnapshot rollup) =>
        new(rollup.BucketAlignment, rollup.BucketSize, BucketBoundary.ResolveZone(rollup.ReferenceTimeZone),
            DayStartFromMs(rollup.DayStartOffsetMs));

    /// <summary>
    /// The bucket calendar of a resolution rung. The rung only knows its grain, so the bucket size
    /// is <see cref="TimeSpan.Zero"/> when the grain is unknown.
    /// </summary>
    public static BucketCalendar For(ResolutionRung rung) =>
        new(rung.Alignment,
            rung.GrainMs is { } grain ? TimeSpan.FromMilliseconds(grain) : TimeSpan.Zero,
            BucketBoundary.ResolveZone(rung.ReferenceTimeZone),
            DayStartFromMs(rung.DayStartOffsetMs));

    /// <summary>The same calendar with another time zone (e.g. the query zone of a read).</summary>
    public BucketCalendar WithZone(TimeZoneInfo? zone) => this with { Zone = zone };

    /// <summary>True when day buckets start at another time than local midnight.</summary>
    public bool HasDayStart => Alignment == BucketAlignment.CalendarDay && DayStart > TimeSpan.Zero;
}
