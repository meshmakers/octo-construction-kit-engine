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
    Sum = 4
}
