using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157 — <see cref="RollupLadderFunctionResolver"/> over a multi-source ladder: a rung whose
/// sources include the requested base resolves directly, otherwise the walk cascades through the
/// first source that is a ladder member and yields origins, so a source outside the family (the
/// legacy archive of a cutover setup) cannot mask the in-family parent.
/// </summary>
public class RollupLadderFunctionResolverTests
{
    private const string Path = "DimmingLevel";
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("EnergyMeasurement"));

    private static readonly OctoObjectId BaseRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId LegacyRt = OctoObjectId.GenerateNewId();
    private static readonly DateTime Cutover = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId rtId,
        RollupSourceReference[] sources,
        params CkRollupAggregationSpec[] aggregations) =>
        new(
            rtId, TargetType, CkArchiveStatus.Activated, null,
            sources,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null,
            aggregations,
            null);

    private static RollupSourceReference[] From(params OctoObjectId[] sourceRtIds) =>
        Array.ConvertAll(sourceRtIds, id => new RollupSourceReference(id));

    private static IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> Ladder(
        params RollupArchiveSnapshot[] rollups)
    {
        var map = new Dictionary<OctoObjectId, RollupArchiveSnapshot>();
        foreach (var rollup in rollups)
        {
            map[rollup.RtId] = rollup;
        }

        return map;
    }

    // TC-REC-09: one of the sources IS the requested base — the rung's own specs are the origins.
    [Fact]
    public void DirectOrigins_WhenAnySourceIsTheRequestedBase()
    {
        var rollup = Rollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(LegacyRt, ValidTo: Cutover),
                new RollupSourceReference(BaseRt, ValidFrom: Cutover),
            },
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            rollup, BaseRt, Path, Ladder(rollup));

        Assert.Equal(new[] { CkRollupFunction.Sum }, functions);
    }

    [Fact]
    public void NoOrigins_WhenNoSourceLeadsToTheRequestedBase()
    {
        var unrelated = OctoObjectId.GenerateNewId();
        var rollup = Rollup(
            OctoObjectId.GenerateNewId(),
            From(unrelated),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));

        Assert.Empty(RollupLadderFunctionResolver.StoredFunctionsFor(rollup, BaseRt, Path, Ladder(rollup)));
    }

    [Fact]
    public void CascadeThroughTheInFamilySource_OutOfFamilySourceDoesNotMaskIt()
    {
        // Monthly reads the legacy archive (outside the family, listed FIRST) and the hourly rung.
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(LegacyRt, ValidTo: Cutover),
                new RollupSourceReference(hourly.RtId, ValidFrom: Cutover),
            },
            new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum"));

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(hourly, monthly));

        Assert.Equal(new[] { CkRollupFunction.Sum }, functions);
    }

    [Fact]
    public void LogicalRungOverAPhysicallyChainedLadder_ResolvesTheLogicalFunction()
    {
        // Read-side counterpart of the write-side bridge: the seeded pre-AB#5157 EC ladder chains by
        // PHYSICAL column name (SourcePath = "dimminglevel_sum", pinned with TargetColumnName). The
        // cascade carries the logical path through every physical hop, so a new LOGICAL rung stacked
        // on top still resolves its function (WI AB#5157 review: the sbeg quarter rung over the seeded
        // EC monthly rung, itself over a physically-chained daily rung). Rooted at the raw base.
        var hourly = Rollup(OctoObjectId.GenerateNewId(), From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var daily = Rollup(OctoObjectId.GenerateNewId(), From(hourly.RtId),
            new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum"));
        var monthly = Rollup(OctoObjectId.GenerateNewId(), From(daily.RtId),
            new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum"));
        var quarterly = Rollup(OctoObjectId.GenerateNewId(), From(monthly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            quarterly, BaseRt, Path, Ladder(hourly, daily, monthly, quarterly));

        Assert.Equal(new[] { CkRollupFunction.Sum }, functions);
    }

    [Fact]
    public void BrokenInFamilyChain_YieldsNoFunctions()
    {
        // The monthly rung applies MIN to a SUM column — not a legal single-step chain, so nothing
        // survives and the rung must not be offered for the path.
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Min, "dimminglevel_sum"));

        Assert.Empty(RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(hourly, monthly)));
    }

    // The sbeg cutover shape: a monthly rung over the multi-source daily rung, TWA pair + SUM.
    [Fact]
    public void SbegShape_ResolvesTimeWeightedAvgAndSumThroughTheMonthlyParent()
    {
        var daily = Rollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(LegacyRt, ValidTo: Cutover),
                new RollupSourceReference(BaseRt, ValidFrom: Cutover),
            },
            new CkRollupAggregationSpec(Path, CkRollupFunction.TimeWeightedAvg, null),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(daily.RtId),
            new CkRollupAggregationSpec("dimminglevel_twavg_integral", CkRollupFunction.Sum, "dimminglevel_twavg_integral"),
            new CkRollupAggregationSpec("dimminglevel_twavg_duration", CkRollupFunction.Sum, "dimminglevel_twavg_duration"),
            new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum"));

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(daily, monthly));

        Assert.Contains(CkRollupFunction.TimeWeightedAvg, functions);
        Assert.Contains(CkRollupFunction.Sum, functions);
    }

    [Fact]
    public void IncompleteTwaPairOnTheCascade_IsNotOffered()
    {
        var daily = Rollup(
            OctoObjectId.GenerateNewId(),
            new[]
            {
                new RollupSourceReference(LegacyRt, ValidTo: Cutover),
                new RollupSourceReference(BaseRt, ValidFrom: Cutover),
            },
            new CkRollupAggregationSpec(Path, CkRollupFunction.TimeWeightedAvg, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(daily.RtId),
            new CkRollupAggregationSpec("dimminglevel_twavg_integral", CkRollupFunction.Sum, "dimminglevel_twavg_integral"));

        Assert.DoesNotContain(
            CkRollupFunction.TimeWeightedAvg,
            RollupLadderFunctionResolver.StoredFunctionsFor(monthly, BaseRt, Path, Ladder(daily, monthly)));
    }

    // ---- Rule 2 (AB#5157): a rung declaring the LOGICAL path over a rollup parent --------------

    [Fact]
    public void LogicalCascadeSpec_OverAParentStoringTheSameFunction_Resolves()
    {
        // The read-side twin of RollupSourceColumnResolver rule 2: the monthly rung names
        // 'DimmingLevel' (not 'dimminglevel_sum'), which is what an AB#5157 multi-source rung over
        // a base archive AND its hourly rollup has to declare.
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(hourly, monthly));

        Assert.Equal(new[] { CkRollupFunction.Sum }, functions.ToArray());
    }

    [Fact]
    public void LogicalCascadeSpec_WithADifferentFunctionThanTheParent_IsDropped()
    {
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Min, null));

        Assert.Empty(RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(hourly, monthly)));
    }

    [Fact]
    public void LogicalCascadeSpec_AvgPair_ResolvesWhenBothParentSlotsExist()
    {
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Avg, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.Avg, null));

        Assert.Equal(
            new[] { CkRollupFunction.Avg },
            RollupLadderFunctionResolver.StoredFunctionsFor(monthly, BaseRt, Path, Ladder(hourly, monthly)).ToArray());
    }

    [Theory]
    [InlineData(CkRollupFunction.First)]
    [InlineData(CkRollupFunction.Last)]
    public void LogicalCascadeSpec_FirstAndLast_Resolve(CkRollupFunction function)
    {
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, function, null));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, function, null));

        Assert.Equal(
            new[] { function },
            RollupLadderFunctionResolver.StoredFunctionsFor(monthly, BaseRt, Path, Ladder(hourly, monthly)).ToArray());
    }

    [Fact]
    public void LogicalCascadeSpec_StateDurationOverAnotherComparedState_IsDropped()
    {
        // The parent measured the time spent in 'false'; re-reading it as the 'true' duration would
        // be a different quantity.
        var hourly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(BaseRt),
            new CkRollupAggregationSpec(Path, CkRollupFunction.StateDuration, null, "false"));
        var monthly = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.StateDuration, null, "true"));

        Assert.Empty(RollupLadderFunctionResolver.StoredFunctionsFor(
            monthly, BaseRt, Path, Ladder(hourly, monthly)));

        var sameState = Rollup(
            OctoObjectId.GenerateNewId(),
            From(hourly.RtId),
            new CkRollupAggregationSpec(Path, CkRollupFunction.StateDuration, null, "false"));

        Assert.Equal(
            new[] { CkRollupFunction.StateDuration },
            RollupLadderFunctionResolver.StoredFunctionsFor(
                sameState, BaseRt, Path, Ladder(hourly, sameState)).ToArray());
    }

    [Fact]
    public void ChainDeeperThanTheDepthCap_YieldsNoFunctions()
    {
        // The cap defends against a corrupt store; a well-formed ladder is 1-4 levels. Build a
        // 12-level SUM chain over the base and check the walk gives up rather than recursing on.
        var ladder = new List<RollupArchiveSnapshot>
        {
            Rollup(
                OctoObjectId.GenerateNewId(),
                From(BaseRt),
                new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null)),
        };
        for (var level = 1; level < 12; level++)
        {
            ladder.Add(Rollup(
                OctoObjectId.GenerateNewId(),
                From(ladder[^1].RtId),
                new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum")));
        }

        var top = ladder[^1];

        Assert.Empty(RollupLadderFunctionResolver.StoredFunctionsFor(
            top, BaseRt, Path, Ladder(ladder.ToArray())));
    }

    [Fact]
    public void ShallowChainWithinTheDepthCap_StillResolves()
    {
        // Counterpart of the depth-cap fact: four levels resolve.
        var ladder = new List<RollupArchiveSnapshot>
        {
            Rollup(
                OctoObjectId.GenerateNewId(),
                From(BaseRt),
                new CkRollupAggregationSpec(Path, CkRollupFunction.Sum, null)),
        };
        for (var level = 1; level < 4; level++)
        {
            ladder.Add(Rollup(
                OctoObjectId.GenerateNewId(),
                From(ladder[^1].RtId),
                new CkRollupAggregationSpec("dimminglevel_sum", CkRollupFunction.Sum, "dimminglevel_sum")));
        }

        var functions = RollupLadderFunctionResolver.StoredFunctionsFor(
            ladder[^1], BaseRt, Path, Ladder(ladder.ToArray()));

        Assert.Equal(new[] { CkRollupFunction.Sum }, functions.ToArray());
    }
}
