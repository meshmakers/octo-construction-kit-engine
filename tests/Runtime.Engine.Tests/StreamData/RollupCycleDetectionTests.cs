using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157 — <see cref="RollupValidator.ValidateNoTransitiveCycle"/>: following source edges from a
/// rollup through the tenant's rollups must never lead back to that rollup. Runs at create (the
/// rollup is not yet in the map) and at activation (it is).
/// </summary>
public class RollupCycleDetectionTests
{
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));

    private static RollupArchiveSnapshot Rollup(OctoObjectId rtId, params OctoObjectId[] sourceRtIds) =>
        new(
            rtId, TargetType, CkArchiveStatus.Activated, null,
            Array.ConvertAll(sourceRtIds, id => new RollupSourceReference(id)),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);

    private static IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> Map(
        params RollupArchiveSnapshot[] rollups)
    {
        var map = new Dictionary<OctoObjectId, RollupArchiveSnapshot>();
        foreach (var rollup in rollups)
        {
            map[rollup.RtId] = rollup;
        }

        return map;
    }

    [Fact]
    public void PlainLadder_NoCycle_DoesNotThrow()
    {
        var raw = OctoObjectId.GenerateNewId();
        var hourly = Rollup(OctoObjectId.GenerateNewId(), raw);
        var daily = Rollup(OctoObjectId.GenerateNewId(), hourly.RtId);

        RollupValidator.ValidateNoTransitiveCycle(daily, Map(hourly, daily));
    }

    [Fact]
    public void SourcesOutsideTheMap_AreTreatedAsLeaves()
    {
        // Raw and time-range archives are not rollups and are legitimately absent from the map.
        var daily = Rollup(OctoObjectId.GenerateNewId(), OctoObjectId.GenerateNewId(), OctoObjectId.GenerateNewId());

        RollupValidator.ValidateNoTransitiveCycle(daily, Map());
    }

    // TC-VAL-17: the rollup listing itself is caught by the graph walk as well.
    [Fact]
    public void DirectSelfReference_Throws()
    {
        var rtId = OctoObjectId.GenerateNewId();
        var rollup = Rollup(rtId, rtId);

        var ex = Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(rollup, Map(rollup)));
        Assert.Equal(rtId, ex.SourceArchiveRtId);
    }

    [Fact]
    public void TwoStepCycle_Throws()
    {
        // a → b → a
        var aId = OctoObjectId.GenerateNewId();
        var bId = OctoObjectId.GenerateNewId();
        var a = Rollup(aId, bId);
        var b = Rollup(bId, aId);

        var ex = Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(a, Map(a, b)));
        Assert.Equal(bId, ex.SourceArchiveRtId);
        Assert.Contains(bId.ToString(), ex.Message);
        Assert.Contains(aId.ToString(), ex.Message);
    }

    // TC-VAL-18: the cycle closes through the SECOND source of a multi-source rung.
    [Fact]
    public void CycleThroughTheSecondSourceOfAMultiSourceRung_Throws()
    {
        var raw = OctoObjectId.GenerateNewId();
        var aId = OctoObjectId.GenerateNewId();
        var bId = OctoObjectId.GenerateNewId();
        // a reads raw and b; b reads raw and a — the cycle only exists via the second source.
        var a = Rollup(aId, raw, bId);
        var b = Rollup(bId, raw, aId);

        var ex = Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(a, Map(a, b)));
        Assert.Equal(bId, ex.SourceArchiveRtId);
    }

    [Fact]
    public void LongerCycle_Throws()
    {
        // a → b → c → a
        var aId = OctoObjectId.GenerateNewId();
        var bId = OctoObjectId.GenerateNewId();
        var cId = OctoObjectId.GenerateNewId();
        var a = Rollup(aId, bId);
        var b = Rollup(bId, cId);
        var c = Rollup(cId, aId);

        var ex = Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(a, Map(a, b, c)));
        Assert.Equal(bId, ex.SourceArchiveRtId);
    }

    [Fact]
    public void CreateTime_RollupNotYetInTheMap_StillDetectsTheCycle()
    {
        // At create time the prospective rollup is not stored yet, but an existing rollup may
        // already point at the rtId the caller passes (an ImportRt-seeded pair).
        var newId = OctoObjectId.GenerateNewId();
        var existingId = OctoObjectId.GenerateNewId();
        var existing = Rollup(existingId, newId);
        var prospective = Rollup(newId, existingId);

        Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(prospective, Map(existing)));
    }

    [Fact]
    public void DiamondWithoutCycle_Terminates()
    {
        // raw → a, raw → b, merged reads a and b; both paths reach raw and stop.
        var raw = OctoObjectId.GenerateNewId();
        var a = Rollup(OctoObjectId.GenerateNewId(), raw);
        var b = Rollup(OctoObjectId.GenerateNewId(), raw);
        var merged = Rollup(OctoObjectId.GenerateNewId(), a.RtId, b.RtId);

        RollupValidator.ValidateNoTransitiveCycle(merged, Map(a, b, merged));
    }

    [Fact]
    public void CycleAmongOtherRollups_ThatDoesNotReachTheValidatedRollup_Terminates()
    {
        // b → c → b is broken, but the validated rollup a only reads raw — the walk must terminate
        // without reporting a cycle for a.
        var raw = OctoObjectId.GenerateNewId();
        var bId = OctoObjectId.GenerateNewId();
        var cId = OctoObjectId.GenerateNewId();
        var a = Rollup(OctoObjectId.GenerateNewId(), raw);
        var b = Rollup(bId, cId);
        var c = Rollup(cId, bId);

        RollupValidator.ValidateNoTransitiveCycle(a, Map(a, b, c));
    }

    [Fact]
    public void CycleReachableThroughAnIntermediateRollup_Throws()
    {
        // a reads raw and b; b reads c; c reads a.
        var raw = OctoObjectId.GenerateNewId();
        var aId = OctoObjectId.GenerateNewId();
        var bId = OctoObjectId.GenerateNewId();
        var cId = OctoObjectId.GenerateNewId();
        var a = Rollup(aId, raw, bId);
        var b = Rollup(bId, cId);
        var c = Rollup(cId, aId);

        var ex = Assert.Throws<RollupSourceCycleException>(
            () => RollupValidator.ValidateNoTransitiveCycle(a, Map(a, b, c)));
        Assert.Equal(bId, ex.SourceArchiveRtId);
    }
}
