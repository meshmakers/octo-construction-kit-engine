namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Bucket-boundary alignment for a <c>RollupArchive</c>. Mirrors the <c>BucketAlignment</c> CK
/// enum (System.StreamData 1.4.0). Lifecycle and orchestrator code reference this enum directly
/// so they don't have to depend on the generated CK enum type.
/// </summary>
/// <remarks>
/// Pre-1.4.0 entities — and any entity that doesn't carry the attribute — resolve to
/// <see cref="FixedSize"/> at read time, which reproduces the legacy
/// <c>LastAggregatedBucketEnd + BucketSizeMs</c> arithmetic. The calendar / ISO-week variants
/// derive boundaries from the wall clock instead, so monthly / weekly / yearly EDA rollups
/// become expressible without forcing every cadence into a fixed <c>TimeSpan</c>. Concept-time-
/// range §7.
/// </remarks>
public enum BucketAlignment
{
    /// <summary>
    /// Legacy default. Boundaries are <c>LastAggregatedBucketEnd</c>,
    /// <c>LastAggregatedBucketEnd + BucketSizeMs</c>, ... — strictly arithmetic, no calendar
    /// awareness. <c>BucketSizeMs</c> fully determines the bucket width.
    /// </summary>
    FixedSize = 0,

    /// <summary>
    /// Bucket boundaries snap to UTC calendar days (00:00:00 UTC). One row per (rtId, day).
    /// <c>BucketSizeMs</c> is informational only.
    /// </summary>
    CalendarDay = 1,

    /// <summary>
    /// Bucket boundaries snap to ISO-8601 weeks (Monday 00:00:00 UTC to next Monday 00:00:00
    /// UTC). One row per (rtId, week). <c>BucketSizeMs</c> is informational only.
    /// </summary>
    Iso8601Week = 2,

    /// <summary>
    /// Bucket boundaries snap to UTC calendar months (first-of-month 00:00:00 UTC).
    /// Month lengths vary 28-31 days; <c>BucketSizeMs</c> is informational only.
    /// </summary>
    CalendarMonth = 3,

    /// <summary>
    /// Bucket boundaries snap to UTC calendar years (Jan 1 00:00:00 UTC). One row per
    /// (rtId, year). <c>BucketSizeMs</c> is informational only.
    /// </summary>
    CalendarYear = 4,
}
