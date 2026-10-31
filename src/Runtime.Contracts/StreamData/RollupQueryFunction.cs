using System;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Which stored rollup aggregation answers a query aggregation function. Used to route a query to a
/// rollup rung (<see cref="SeriesResolutionRequest.ForQueryFunction"/>) without the caller knowing the
/// storage layout.
/// </summary>
/// <remarks>
/// Every function needs the same function stored, except <see cref="AggregationFunction.Integral"/>:
/// a rollup answers it from the integral half of a <see cref="CkRollupFunction.TimeWeightedAvg"/>
/// pair (Σ value × Δt per bucket), so Integral is served by exactly the rollups that serve
/// TimeWeightedAverage. A sample <see cref="CkRollupFunction.Avg"/> or <see cref="CkRollupFunction.Sum"/>
/// has no time weighting and never answers Integral (AB#7920).
/// </remarks>
public static class RollupQueryFunction
{
    /// <summary>
    /// The rollup aggregation a rung must store for <paramref name="queryFunction"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="AggregationFunction"/>.</exception>
    public static CkRollupFunction RequiredStoredFunction(AggregationFunction queryFunction) =>
        queryFunction switch
        {
            AggregationFunction.Count => CkRollupFunction.Count,
            AggregationFunction.Minimum => CkRollupFunction.Min,
            AggregationFunction.Maximum => CkRollupFunction.Max,
            AggregationFunction.Average => CkRollupFunction.Avg,
            AggregationFunction.Sum => CkRollupFunction.Sum,
            AggregationFunction.TimeWeightedAverage => CkRollupFunction.TimeWeightedAvg,
            AggregationFunction.StateDuration => CkRollupFunction.StateDuration,
            AggregationFunction.Integral => CkRollupFunction.TimeWeightedAvg,
            _ => throw new ArgumentOutOfRangeException(nameof(queryFunction), queryFunction,
                "No rollup aggregation is defined for this query function."),
        };
}
