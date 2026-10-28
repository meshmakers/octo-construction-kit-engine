using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#7727 — which rollup functions make sense over the row origin fields.
/// </summary>
public class ArchiveRowOriginFieldsTests
{
    [Theory]
    [InlineData("row_origin", true)]
    [InlineData("manual_written_by", true)]
    [InlineData("Manual_Written_At", true)]
    [InlineData("manual_reason", true)]
    [InlineData("manualwrittenat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOriginField(string? path, bool expected)
    {
        Assert.Equal(expected, ArchiveRowOriginFields.IsOriginField(path));
    }

    [Theory]
    [InlineData("manual_written_at", CkRollupFunction.Count, true)]
    [InlineData("manual_written_at", CkRollupFunction.Min, true)]
    [InlineData("manual_written_at", CkRollupFunction.Max, true)]
    [InlineData("manual_written_at", CkRollupFunction.Sum, false)]
    [InlineData("manual_written_at", CkRollupFunction.Avg, false)]
    [InlineData("manual_written_at", CkRollupFunction.TimeWeightedAvg, false)]
    [InlineData("manual_written_at", CkRollupFunction.StateDuration, false)]
    [InlineData("manual_written_at", CkRollupFunction.First, false)]
    [InlineData("manual_written_at", CkRollupFunction.Last, false)]
    [InlineData("manual_written_by", CkRollupFunction.Count, true)]
    [InlineData("manual_written_by", CkRollupFunction.Min, false)]
    [InlineData("manual_reason", CkRollupFunction.Count, true)]
    [InlineData("manual_reason", CkRollupFunction.Sum, false)]
    [InlineData("row_origin", CkRollupFunction.Count, false)]
    [InlineData("Amount.Value", CkRollupFunction.Sum, false)]
    public void SupportsRollupAggregation(string path, CkRollupFunction function, bool expected)
    {
        Assert.Equal(expected, ArchiveRowOriginFields.SupportsRollupAggregation(path, function));
    }

    [Theory]
    [InlineData("manual_written_at", CkRollupFunction.Max, true)]
    [InlineData("manual_written_at", CkRollupFunction.Min, true)]
    [InlineData("manual_written_at", CkRollupFunction.Count, false)]
    [InlineData("manual_written_at_max", CkRollupFunction.Max, false)]
    [InlineData("Amount.Value", CkRollupFunction.Max, false)]
    public void IsTimestampAggregation(string path, CkRollupFunction function, bool expected)
    {
        Assert.Equal(expected, ArchiveRowOriginFields.IsTimestampAggregation(path, function));
    }
}
