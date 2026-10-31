namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Aggregation functions supported by the runtime query contracts layer.
///     Mirrors the values of the CK-generated System <c>AggregationTypes</c> enum and the
///     transport-level <c>AggregationTypesDto</c>. Defined here to keep Runtime.Contracts
///     self-contained (no dependency on generated System CK model types).
/// </summary>
public enum AggregationFunction
{
    /// <summary>Count of non-null values.</summary>
    Count = 0,

    /// <summary>Minimum value.</summary>
    Minimum = 1,

    /// <summary>Maximum value.</summary>
    Maximum = 2,

    /// <summary>Arithmetic mean.</summary>
    Average = 3,

    /// <summary>Sum of values.</summary>
    Sum = 4,

    /// <summary>
    /// Time-weighted average (LOCF interval weighting) for event-based archives. Resolvable
    /// against rollup archives that materialise a <c>TimeWeightedAvg</c> aggregation
    /// (integral/duration pair). AB#4336.
    /// </summary>
    TimeWeightedAverage = 5,

    /// <summary>
    /// Absolute time (milliseconds) an event-based signal held the column's
    /// <see cref="AggregationColumn.ComparisonValue"/> within the window, with LOCF semantics.
    /// Supported over raw event archives (query-time LOCF) and rollup archives that materialise
    /// a <c>StateDuration</c> aggregation. AB#4336 / AB#4341.
    /// </summary>
    StateDuration = 6,

    /// <summary>
    /// Step integral ∫v dt of a numeric event-based signal over the half-open query window
    /// <c>[from, to)</c>, in <em>value unit × hours</em> (kW → kWh, W → Wh). Last observation carried
    /// forward: each value holds until the next row of the same <c>rtId</c> or the window end, and
    /// the last row before <c>from</c> opens the window; that carry-in row is looked up at most 35 days
    /// back (engine default lookback), an older row is not carried. Several rtIds in scope are summed.
    /// An empty window (no row in the window and no carry-in row) yields <c>null</c>, and so does a
    /// window whose rows all carry no value (only NULL observations: no measured energy, not zero). Requires both
    /// <c>from</c> and <c>to</c>. Served over raw event archives at query time (AB#7918) and over
    /// rollup archives that materialise a <c>TimeWeightedAvg</c> aggregation on the column, read as
    /// the sum of the stored per-bucket integrals over the buckets that overlap the window (AB#7920).
    /// </summary>
    Integral = 7
}
