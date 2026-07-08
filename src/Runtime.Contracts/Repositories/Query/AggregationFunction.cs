namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Aggregation functions supported by the runtime query contracts layer.
///     Mirrors the five values of the CK-generated System <c>AggregationTypes</c> enum and the
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
    TimeWeightedAverage = 5
}
