using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// One candidate archive in a series' resolution family — either the base archive (raw or
/// time-range) or one of its rollups. The <c>SeriesResolutionPlanner</c> reasons purely over a list
/// of these to pick the archive to query. See <c>concept-resolution-aware-series-queries.md</c> §4.
/// </summary>
/// <param name="ArchiveRtId">The archive entity id to query if this rung is chosen.</param>
/// <param name="GrainMs">
/// The rung's native bucket width in milliseconds — a rollup's <c>BucketSize</c>, or a
/// time-range base archive's <c>Period</c>. <c>null</c> when the grain is unknown (a raw archive, or
/// a time-range archive whose advisory <c>Period</c> is unset); such a rung is treated as "finest,
/// unknown resolution".
/// </param>
/// <param name="Alignment">
/// Bucket alignment. <see cref="BucketAlignment.FixedSize"/> means <see cref="GrainMs"/> fully
/// determines the width; calendar variants derive an effective width from the wall clock (and the
/// reference time zone) instead.
/// </param>
/// <param name="StoredFunctionForSeries">
/// The aggregation function this rung stores for the requested series' column, or <c>null</c> for a
/// base archive (which stores no aggregation). A rollup rung is a valid reduction source only when
/// this equals the caller's requested aggregation.
/// </param>
/// <param name="IsBase">
/// True for the base archive (raw or time-range) the rollups are derived from; false for rollups.
/// The base is never reduced by the resolver (decision O2-followup) — it is returned unreduced when
/// no compatible rollup exists, or when its native data already fits the requested point count.
/// </param>
public sealed record ResolutionRung(
    OctoObjectId ArchiveRtId,
    long? GrainMs,
    BucketAlignment Alignment,
    CkRollupFunction? StoredFunctionForSeries,
    bool IsBase)
{
    /// <summary>
    /// IANA time-zone id used to align calendar buckets (day/month/year) to local wall-clock
    /// boundaries. <c>null</c> ⇒ UTC. Only consulted for calendar <see cref="Alignment"/> variants.
    /// (AB#4290 Phase 4 / decision O6.)
    /// </summary>
    public string? ReferenceTimeZone { get; init; }
}
