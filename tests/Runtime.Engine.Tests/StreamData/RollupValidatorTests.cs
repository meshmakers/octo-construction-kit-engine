using System;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupValidatorTests
{
    private static readonly OctoObjectId RollupRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId? sourceRt = null,
        CkRollupAggregationSpec[]? aggregations = null) =>
        new(
            RollupRt, TargetType, CkArchiveStatus.Created, null,
            sourceRt ?? SourceRt,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
            null,
            aggregations ?? new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);

    private static ArchiveSnapshot Source(
        CkArchiveStatus status = CkArchiveStatus.Activated,
        params string[] paths) =>
        new(
            SourceRt, TargetType, status, null,
            Array.ConvertAll(paths, p => new CkArchiveColumnSpec(p, true, false)));

    // ---- ValidateForSave ----

    [Fact]
    public void ValidateForSave_HappyPath_DoesNotThrow()
    {
        RollupValidator.ValidateForSave(Rollup());
    }

    [Fact]
    public void ValidateForSave_NoAggregations_Throws()
    {
        var rollup = Rollup(aggregations: Array.Empty<CkRollupAggregationSpec>());

        Assert.Throws<RollupAggregationsRequiredException>(
            () => RollupValidator.ValidateForSave(rollup));
    }

    [Fact]
    public void ValidateForSave_DirectSelfCycle_Throws()
    {
        var rollup = Rollup(sourceRt: RollupRt);

        Assert.Throws<RollupCycleException>(() => RollupValidator.ValidateForSave(rollup));
    }

    [Fact]
    public void ValidateForSave_DuplicatePathFunction_Throws()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null),
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, "different_name"),
        });

        var ex = Assert.Throws<DuplicateRollupAggregationException>(
            () => RollupValidator.ValidateForSave(rollup));
        Assert.Equal("voltage", ex.SourcePath);
        Assert.Equal(CkRollupFunction.Avg, ex.Function);
    }

    [Fact]
    public void ValidateForSave_SamePathDifferentFunctions_OK()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Min, null),
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Max, null),
        });

        RollupValidator.ValidateForSave(rollup);
    }

    // ---- ValidateForActivation ----

    [Fact]
    public void ValidateForActivation_HappyPath_DoesNotThrow()
    {
        RollupValidator.ValidateForActivation(Rollup(), Source(paths: "voltage"));
    }

    [Fact]
    public void ValidateForActivation_SourceNull_ThrowsRollupSourceMissing()
    {
        Assert.Throws<RollupSourceMissingException>(
            () => RollupValidator.ValidateForActivation(Rollup(), source: null));
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Disabled)]
    [InlineData(CkArchiveStatus.Failed)]
    public void ValidateForActivation_SourceNotActivated_Throws(CkArchiveStatus status)
    {
        var ex = Assert.Throws<RollupSourceNotActivatedException>(
            () => RollupValidator.ValidateForActivation(Rollup(), Source(status, "voltage")));
        Assert.Equal(status, ex.SourceStatus);
    }

    [Fact]
    public void ValidateForActivation_SourcePathMissing_Throws()
    {
        var ex = Assert.Throws<RollupSourcePathInvalidException>(
            () => RollupValidator.ValidateForActivation(Rollup(), Source(paths: "current")));
        Assert.Equal("voltage", ex.SourcePath);
    }

    [Fact]
    public void ValidateForActivation_AlsoRunsSaveTimeChecks()
    {
        var rollup = Rollup(aggregations: Array.Empty<CkRollupAggregationSpec>());

        Assert.Throws<RollupAggregationsRequiredException>(
            () => RollupValidator.ValidateForActivation(rollup, Source(paths: "voltage")));
    }
}
