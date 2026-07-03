namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// How civil-time bucket boundaries are resolved when one series query spans metering points in
/// different reference time zones (AB#4190, decision T2). See
/// <c>concept-timezone-aware-queries.md</c> §5.
/// </summary>
public enum SeriesComparisonPolicy
{
    /// <summary>
    /// Default. A single query time zone is applied uniformly to every series — "yesterday" means one
    /// civil day in the query zone, the least-surprising choice for a single operator comparing sites.
    /// A calendar rung is a valid civil-day source only when its stored
    /// <see cref="RollupArchiveSnapshot.ReferenceTimeZone"/> matches the query zone; a mismatched
    /// calendar rung holds a different zone's civil buckets and is therefore excluded from selection.
    /// </summary>
    PerQuery = 0,

    /// <summary>
    /// Each series resolves its own local day/week/month in its archive's
    /// <see cref="RollupArchiveSnapshot.ReferenceTimeZone"/> — "yesterday" is each point's own local
    /// yesterday. Reuses the existing per-source-rtId fan-out; every calendar rung aligns to its own
    /// stored zone, so no zone-match exclusion applies.
    /// </summary>
    PerSeries = 1,
}
