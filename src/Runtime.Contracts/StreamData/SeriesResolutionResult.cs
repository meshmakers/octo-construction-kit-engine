using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The resolver's routing decision (shape A1): which archive to query, at what effective bucket
/// width, how many points to expect, with which reducer, and a signal describing the outcome. The
/// caller then issues the existing downsampling query (AB#4233) against <see cref="ArchiveRtId"/>
/// with <c>limit = Points</c> and the column aggregation set to <see cref="ReducingFunction"/>.
/// See <c>concept-resolution-aware-series-queries.md</c> §5.1.
/// </summary>
/// <param name="ArchiveRtId">The archive to query — a rollup, or the base archive for the refuse/raw paths.</param>
/// <param name="EffectiveBucketMs">
/// The width in milliseconds of one output bucket. For a reduced result this is
/// <c>(To - From) / Points</c>; for an unreduced base result it is the base's native grain, or 0 when
/// no bucketing applies / the grain is unknown.
/// </param>
/// <param name="Points">The number of points the caller can expect from the downsampling query.</param>
/// <param name="ReducingFunction">The aggregation function the downsampling query must use.</param>
/// <param name="Signal">The outcome classification (see <see cref="SeriesResolutionSignal"/>).</param>
public sealed record SeriesResolutionResult(
    OctoObjectId ArchiveRtId,
    long EffectiveBucketMs,
    int Points,
    CkRollupFunction ReducingFunction,
    SeriesResolutionSignal Signal)
{
    /// <summary>
    /// The actually deliverable point count when it is below the requested target
    /// (<see cref="SeriesResolutionSignal.ResolutionLimited"/>) or the native raw count on the
    /// refuse path. Null when the target was met.
    /// </summary>
    public int? ActualPoints { get; init; }

    /// <summary>Optional human-readable explanation of the chosen route / signal.</summary>
    public string? Diagnostic { get; init; }
}
