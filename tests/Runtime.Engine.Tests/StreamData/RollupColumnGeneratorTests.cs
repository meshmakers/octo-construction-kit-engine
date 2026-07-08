using System;
using System.Linq;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupColumnGeneratorTests
{
    [Fact]
    public void Generate_AvgSpec_ProducesSumAndCountColumns()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Equal(2, columns.Count);
        Assert.Equal("voltage_avg_sum", columns[0].Path);
        Assert.Equal("voltage_avg_count", columns[1].Path);
        Assert.All(columns, c => Assert.True(c.Indexed));
        Assert.All(columns, c => Assert.False(c.Required));
    }

    [Theory]
    [InlineData(CkRollupFunction.Min, "voltage_min")]
    [InlineData(CkRollupFunction.Max, "voltage_max")]
    [InlineData(CkRollupFunction.Sum, "voltage_sum")]
    [InlineData(CkRollupFunction.Count, "voltage_count")]
    public void Generate_SimpleSpec_ProducesSingleColumn(CkRollupFunction fn, string expectedName)
    {
        var aggregations = new[] { new CkRollupAggregationSpec("voltage", fn, null) };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Single(columns);
        Assert.Equal(expectedName, columns[0].Path);
    }

    [Fact]
    public void Generate_ExplicitTargetColumnName_OverridesDefault()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Max, "vmax") };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Single(columns);
        Assert.Equal("vmax", columns[0].Path);
    }

    [Fact]
    public void Generate_ExplicitName_StillSplitsAvgIntoSumCount()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, "v") };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Equal(new[] { "v_sum", "v_count" }, columns.Select(c => c.Path));
    }

    [Fact]
    public void Generate_DottedSourcePath_CollapsesToLowercase()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("Sensor.Reading.Value", CkRollupFunction.Sum, null) };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Single(columns);
        Assert.Equal("sensorreadingvalue_sum", columns[0].Path);
    }

    [Fact]
    public void Generate_MultiSpec_ConcatenatesInOrder()
    {
        var aggregations = new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null),
            new CkRollupAggregationSpec("current", CkRollupFunction.Min, null),
            new CkRollupAggregationSpec("current", CkRollupFunction.Max, null),
        };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Equal(
            new[] { "voltage_avg_sum", "voltage_avg_count", "current_min", "current_max" },
            columns.Select(c => c.Path));
    }

    [Fact]
    public void Generate_DuplicateTargetColumns_Throws()
    {
        var aggregations = new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Max, "v"),
            new CkRollupAggregationSpec("current", CkRollupFunction.Min, "v"),
        };

        Assert.Throws<ArgumentException>(() => RollupColumnGenerator.Generate(aggregations));
    }

    [Fact]
    public void Generate_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RollupColumnGenerator.Generate(null!));
    }

    // ---- TimeWeightedAvg (AB#4336) ----

    [Fact]
    public void Generate_TimeWeightedAvgSpec_ProducesIntegralAndDurationColumns_WithShortToken()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("dimming.level", CkRollupFunction.TimeWeightedAvg, null) };

        var columns = RollupColumnGenerator.Generate(aggregations);

        // Default base name uses the short token "twavg" (decision D5), not the enum name.
        Assert.Equal(2, columns.Count);
        Assert.Equal("dimminglevel_twavg_integral", columns[0].Path);
        Assert.Equal("dimminglevel_twavg_duration", columns[1].Path);
    }

    [Fact]
    public void Generate_TimeWeightedAvgSpec_ExplicitTargetColumnName_UsedAsBase()
    {
        var aggregations = new[] { new CkRollupAggregationSpec("dimming.level", CkRollupFunction.TimeWeightedAvg, "Burn") };

        var columns = RollupColumnGenerator.Generate(aggregations);

        Assert.Equal(2, columns.Count);
        Assert.Equal("burn_integral", columns[0].Path);
        Assert.Equal("burn_duration", columns[1].Path);
    }
}
