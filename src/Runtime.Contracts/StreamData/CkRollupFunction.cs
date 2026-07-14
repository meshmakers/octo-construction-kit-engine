namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Aggregation function applied by a <c>CkRollupAggregation</c>. Mirrors the values of the
/// <c>CkRollupFunction</c> CK enum from the StreamData CK model so lifecycle / orchestrator code
/// does not have to depend on the generated CK enum.
/// </summary>
/// <remarks>
/// <see cref="Avg"/> is materialised at storage time as two columns (<c>{name}_sum</c> and
/// <c>{name}_count</c>) so chained re-aggregation (rollup-of-a-rollup) stays numerically correct.
/// The single-column average is computed on read as <c>sum / NULLIF(count, 0)</c>.
/// <see cref="TimeWeightedAvg"/> follows the same pattern with an integral/duration pair —
/// see <c>concept-time-weighted-aggregation.md</c> (AB#4336).
/// </remarks>
public enum CkRollupFunction
{
    /// <summary>Arithmetic mean. Stored as sum + count; the average is computed on read.</summary>
    Avg = 0,

    /// <summary>Minimum value in the bucket.</summary>
    Min = 1,

    /// <summary>Maximum value in the bucket.</summary>
    Max = 2,

    /// <summary>Sum of values in the bucket.</summary>
    Sum = 3,

    /// <summary>Number of non-null values in the bucket.</summary>
    Count = 4,

    /// <summary>
    /// Time-weighted average with last-observation-carried-forward interval weighting, for
    /// event-based sources (one row per state change). Stored as an integral (Σ value × Δt in
    /// value·milliseconds) + covered-duration (milliseconds) pair; the average is computed on
    /// read as <c>integral / NULLIF(duration, 0)</c>. On a 0/100 or boolean-like signal this
    /// yields the duty cycle per bucket. AB#4336.
    /// </summary>
    TimeWeightedAvg = 5,

    /// <summary>
    /// Absolute time (milliseconds) the signal held the aggregation's
    /// <c>ComparisonValue</c> within the bucket, with the same LOCF carry as
    /// <see cref="TimeWeightedAvg"/>. Stored as a single BIGINT column; chained rollups
    /// re-aggregate it with SUM. AB#4336.
    /// </summary>
    StateDuration = 6,

    /// <summary>
    /// Value of the source column at the earliest timestamp in the bucket (arg_min over time).
    /// Stored as a single column; forward aggregation over a raw source picks the value at
    /// <c>MIN(timestamp)</c>, cascades pick the child bucket with the smallest window boundary.
    /// Numeric source columns only. AB#4188.
    /// </summary>
    First = 7,

    /// <summary>
    /// Value of the source column at the latest timestamp in the bucket (arg_max over time).
    /// Stored as a single column; forward aggregation over a raw source picks the value at
    /// <c>MAX(timestamp)</c>, cascades pick the child bucket with the largest window boundary.
    /// Numeric source columns only. AB#4188.
    /// </summary>
    Last = 8,
}
