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
}
