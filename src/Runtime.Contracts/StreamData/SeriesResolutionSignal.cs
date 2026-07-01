namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Outcome classification returned by the resolution-aware series resolver
/// (<c>SeriesResolutionPlanner</c> / <c>ISeriesResolutionService</c>). The resolver never silently
/// produces a wrong or degraded reduction — every non-<see cref="Ok"/> value is a truthful signal
/// the caller can surface. See <c>concept-resolution-aware-series-queries.md</c> §4.2.
/// </summary>
public enum SeriesResolutionSignal
{
    /// <summary>
    /// A suitable rung was found. Either a rollup whose stored function matches the requested
    /// aggregation was reduced to the requested point count, or the base archive already fits within
    /// the requested point count and is returned unreduced.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The series is reducible only through a rollup whose stored function matches the requested
    /// aggregation, but no such rollup exists (there are none, or only rollups with an incompatible
    /// function — e.g. only <c>Avg</c> rollups for a <c>Sum</c> energy series). The resolver refuses
    /// to reduce the raw archive itself (decision O2-followup) and returns the base archive
    /// unreduced. The caller should provision a matching rollup or query raw explicitly.
    /// </summary>
    NoSuitableRollup = 1,

    /// <summary>
    /// A compatible rollup was found but even its finest rung is coarser than the requested
    /// resolution, so fewer than the requested points are returned (decision O4). The resolver does
    /// not fall through to a finer, costlier source. <c>ActualPoints</c> carries the delivered count.
    /// </summary>
    ResolutionLimited = 2,

    /// <summary>
    /// No compatible rollup exists and the base archive's native grain is not declared
    /// (advisory/absent <c>Period</c> on a raw or time-range archive), so the resolver cannot decide
    /// whether reduction is even needed. The base archive is returned; the caller queries it
    /// directly. Once the base grain is authoritative (AB#4289) this collapses into
    /// <see cref="Ok"/> / <see cref="NoSuitableRollup"/>.
    /// </summary>
    UnknownBaseGrain = 3,

    /// <summary>No archive at all was resolvable for the request (empty family / unknown base).</summary>
    EmptyLadder = 4,
}
