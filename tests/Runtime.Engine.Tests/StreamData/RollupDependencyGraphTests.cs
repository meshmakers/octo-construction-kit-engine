using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupDependencyGraphTests
{
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));

    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();

    private RollupDependencyGraph NewSut() => new(_rollupStore);

    private static RollupArchiveSnapshot Rollup(OctoObjectId rtId, OctoObjectId sourceRtId) =>
        new(
            rtId,
            TargetType,
            CkArchiveStatus.Activated,
            null,
            sourceRtId,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);

    private void StubRollups(params RollupArchiveSnapshot[] rollups) =>
        A.CallTo(() => _rollupStore.EnumerateAsync()).Returns(ToAsync(rollups));

    private static async IAsyncEnumerable<T> ToAsync<T>(T[] items)
    {
        foreach (var item in items) { yield return item; await Task.Yield(); }
    }

    [Fact]
    public async Task NoRollups_ReturnsEmpty()
    {
        var source = OctoObjectId.GenerateNewId();
        StubRollups();

        var result = await NewSut().GetTransitiveDependentsAsync(source);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DirectDependents_ReturnsAllRollupsOnSource()
    {
        var source = OctoObjectId.GenerateNewId();
        var r1 = Rollup(OctoObjectId.GenerateNewId(), source);
        var r2 = Rollup(OctoObjectId.GenerateNewId(), source);
        var unrelated = Rollup(OctoObjectId.GenerateNewId(), OctoObjectId.GenerateNewId());
        StubRollups(r1, r2, unrelated);

        var result = await NewSut().GetTransitiveDependentsAsync(source);

        Assert.Equal(new[] { r1.RtId, r2.RtId }.OrderBy(x => x), result.Select(r => r.RtId).OrderBy(x => x));
        Assert.DoesNotContain(unrelated.RtId, result.Select(r => r.RtId));
    }

    [Fact]
    public async Task Chain_ReturnsRollupOfRollupTopDown()
    {
        // raw → r1 → r2 → r3
        var raw = OctoObjectId.GenerateNewId();
        var r1 = Rollup(OctoObjectId.GenerateNewId(), raw);
        var r2 = Rollup(OctoObjectId.GenerateNewId(), r1.RtId);
        var r3 = Rollup(OctoObjectId.GenerateNewId(), r2.RtId);
        StubRollups(r3, r2, r1); // deliberately out of order in the store

        var result = await NewSut().GetTransitiveDependentsAsync(raw);

        Assert.Equal(new[] { r1.RtId, r2.RtId, r3.RtId }, result.Select(r => r.RtId));
    }

    [Fact]
    public async Task Diamond_VisitsEachRollupOnce()
    {
        // raw → a, raw → b, both a and b → c (two paths to c)
        var raw = OctoObjectId.GenerateNewId();
        var a = Rollup(OctoObjectId.GenerateNewId(), raw);
        var b = Rollup(OctoObjectId.GenerateNewId(), raw);
        var c = Rollup(OctoObjectId.GenerateNewId(), a.RtId);
        var cViaB = c with { SourceArchiveRtId = b.RtId };
        StubRollups(a, b, c, cViaB);

        var result = await NewSut().GetTransitiveDependentsAsync(raw);

        Assert.Equal(1, result.Count(r => r.RtId == c.RtId));
        Assert.Contains(a.RtId, result.Select(r => r.RtId));
        Assert.Contains(b.RtId, result.Select(r => r.RtId));
    }

    [Fact]
    public async Task Cycle_DoesNotLoopForever()
    {
        // Defensive: model forbids cycles, but a corrupt r1 → r2 → r1 must still terminate.
        var raw = OctoObjectId.GenerateNewId();
        var r1Id = OctoObjectId.GenerateNewId();
        var r2Id = OctoObjectId.GenerateNewId();
        var r1 = Rollup(r1Id, raw);
        var r2 = Rollup(r2Id, r1Id);
        var r1Cycle = r1 with { SourceArchiveRtId = r2Id }; // r1 also claims r2 as source
        StubRollups(r1, r2, r1Cycle);

        var result = await NewSut().GetTransitiveDependentsAsync(raw);

        Assert.Equal(new[] { r1Id, r2Id }.OrderBy(x => x), result.Select(r => r.RtId).Distinct().OrderBy(x => x));
    }
}
