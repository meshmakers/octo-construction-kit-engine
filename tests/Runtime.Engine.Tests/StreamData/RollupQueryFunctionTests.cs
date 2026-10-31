using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#7920: which stored rollup function answers a query function. Integral reads the integral
/// half of a TimeWeightedAvg pair; every other function needs the same function stored.
/// </summary>
public class RollupQueryFunctionTests
{
    [Theory]
    [InlineData(AggregationFunction.Integral, CkRollupFunction.TimeWeightedAvg)]
    [InlineData(AggregationFunction.TimeWeightedAverage, CkRollupFunction.TimeWeightedAvg)]
    [InlineData(AggregationFunction.StateDuration, CkRollupFunction.StateDuration)]
    [InlineData(AggregationFunction.Average, CkRollupFunction.Avg)]
    [InlineData(AggregationFunction.Sum, CkRollupFunction.Sum)]
    [InlineData(AggregationFunction.Count, CkRollupFunction.Count)]
    [InlineData(AggregationFunction.Minimum, CkRollupFunction.Min)]
    [InlineData(AggregationFunction.Maximum, CkRollupFunction.Max)]
    public void RequiredStoredFunction_MapsEveryQueryFunction(AggregationFunction query, CkRollupFunction stored)
    {
        Assert.Equal(stored, RollupQueryFunction.RequiredStoredFunction(query));
    }

    [Fact]
    public void RequiredStoredFunction_CoversEveryAggregationFunction()
    {
        // A new AggregationFunction value must decide which rollup function serves it.
        foreach (var function in Enum.GetValues<AggregationFunction>())
        {
            RollupQueryFunction.RequiredStoredFunction(function);
        }
    }

    [Fact]
    public void RequiredStoredFunction_UnknownValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RollupQueryFunction.RequiredStoredFunction((AggregationFunction)99));
    }
}
