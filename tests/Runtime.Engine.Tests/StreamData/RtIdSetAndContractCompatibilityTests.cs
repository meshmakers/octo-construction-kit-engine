using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#9016: the value semantics of <see cref="RtIdSet"/>, and the guarantees that keep a mixed package set safe
/// (engine of AB#9016 with an engine-mongodb built against the previous contract): every positional constructor keeps
/// its signature, the new executor member has a default implementation, and the orchestrator keeps its old constructor.
/// </summary>
public class RtIdSetAndContractCompatibilityTests
{
    private static readonly OctoObjectId M1 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId M2 = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId M3 = OctoObjectId.GenerateNewId();

    [Fact]
    public void RtIdSet_ComparesByMembers_WhateverTheOrderAndDuplicates()
    {
        var a = RtIdSet.Of(new[] { M2, M1, M2 });
        var b = RtIdSet.Of(new[] { M1, M2 });

        Assert.Equal(a, b);
        Assert.Equal(a!.GetHashCode(), b!.GetHashCode());
        Assert.Equal(2, a.Count);
        Assert.NotEqual(a, RtIdSet.Of(new[] { M1 }));
    }

    [Fact]
    public void RtIdSet_OfNothing_IsNoScope()
    {
        Assert.Null(RtIdSet.Of(Array.Empty<OctoObjectId>()));
        Assert.Null(RtIdSet.Of(new[] { OctoObjectId.Empty }));
        Assert.Null(RtIdSet.Of(null));
        Assert.Null(RtIdSet.Parse(new[] { "", null, "not-an-id" }));
    }

    [Fact]
    public void RtIdSet_Union_OfSets_And_NullAbsorbsEverything()
    {
        Assert.Equal(RtIdSet.Of(new[] { M1, M2, M3 }),
            RtIdSet.Union(new[] { RtIdSet.Of(new[] { M1 }), RtIdSet.Of(new[] { M3, M2 }) }));
        Assert.Null(RtIdSet.Union(new[] { RtIdSet.Of(new[] { M1 }), null }));
        Assert.Null(RtIdSet.Union(Array.Empty<RtIdSet?>()));
    }

    [Fact]
    public void RtIdSet_RoundTripsThroughTheStoredStrings()
    {
        var set = RtIdSet.Of(new[] { M3, M1 })!;

        Assert.Equal(set, RtIdSet.Parse(set.ToStrings()));
        Assert.True(RtIdSet.Of(new[] { M1 })!.IsSubsetOf(set));
        Assert.False(RtIdSet.Of(new[] { M2 })!.IsSubsetOf(set));
    }

    [Fact]
    public void ARecomputeRange_KeepsValueEquality_WithItsSet()
    {
        var now = DateTime.UtcNow;
        var rollup = OctoObjectId.GenerateNewId();
        var a = new ArchiveRecomputeRange(rollup, now, now.AddHours(1), null, now) { RtIdSet = RtIdSet.Of(new[] { M1, M2 }) };
        var b = new ArchiveRecomputeRange(rollup, now, now.AddHours(1), null, now) { RtIdSet = RtIdSet.Of(new[] { M2, M1 }) };
        var unscoped = new ArchiveRecomputeRange(rollup, now, now.AddHours(1), null, now);

        Assert.Equal(a, b);
        Assert.NotEqual(a, unscoped);
        Assert.Contains(b, new HashSet<ArchiveRecomputeRange> { a });
    }

    // ---- Mixed package set ----------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(ArchiveDirtyWindow), new[] { typeof(DateTime), typeof(DateTime), typeof(RecomputeChangeKind), typeof(RecomputeChangeSource), typeof(DateTime) })]
    [InlineData(typeof(ArchiveRecomputeRange), new[] { typeof(OctoObjectId), typeof(DateTime), typeof(DateTime), typeof(OctoObjectId?), typeof(DateTime), typeof(int), typeof(DateTime?), typeof(string) })]
    [InlineData(typeof(RecomputeJobSnapshot), new[] { typeof(OctoObjectId), typeof(OctoObjectId), typeof(RecomputeJobState), typeof(RecomputeTrigger), typeof(DateTime), typeof(DateTime), typeof(OctoObjectId?), typeof(int?), typeof(int?), typeof(DateTime?), typeof(DateTime?), typeof(int?), typeof(string), typeof(string), typeof(DateTime?) })]
    public void TheContractRecords_KeepThePositionalConstructorOfThePreviousContract(Type record, Type[] parameters)
    {
        // A storage layer compiled before AB#9016 calls exactly this constructor; a changed signature would be a
        // MissingMethodException at run time in a mixed package set.
        Assert.NotNull(record.GetConstructor(parameters));
    }

    [Fact]
    public void TheOrchestrator_KeepsTheConstructorOfThePreviousContract()
    {
        var parameters = new[]
        {
            typeof(string), typeof(IArchiveRuntimeStore), typeof(IRollupArchiveRuntimeStore), typeof(IRollupDependencyGraph),
            typeof(IArchiveRecomputeStateStore), typeof(IRecomputeJobStore), typeof(IArchiveRecomputeExecutor),
            typeof(IStreamDataRepository), typeof(IArchiveAuditTrail), typeof(ILogger<RecomputeOrchestrator>),
            typeof(Func<DateTime>), typeof(int), typeof(int), typeof(TimeSpan?), typeof(Func<TimeSpan, CancellationToken, Task>),
            typeof(IArchiveCoverageInvalidator), typeof(int), typeof(TimeSpan?), typeof(TimeSpan?),
        };

        Assert.NotNull(typeof(RecomputeOrchestrator).GetConstructor(parameters));
    }

    [Fact]
    public void TheScopedRefreshMember_HasADefaultImplementation()
    {
        var method = typeof(IArchiveRecomputeExecutor).GetMethod(nameof(IArchiveRecomputeExecutor.ExecuteScopedRefreshAsync))!;

        // Not abstract: an executor compiled against the previous contract still satisfies the interface.
        Assert.False(method.IsAbstract);
    }

    [Fact]
    public async Task TheDefaultScopedRefresh_RecomputesOneRtIdScoped_AndSeveralUnscopedOnce()
    {
        var executor = new PreviousContractExecutor();
        var now = DateTime.UtcNow;

        await ((IArchiveRecomputeExecutor)executor).ExecuteScopedRefreshAsync(
            A.Dummy<ArchiveSnapshot>(), A.Dummy<RollupArchiveSnapshot>(), now, now.AddHours(1), RtIdSet.Of(new[] { M1 })!,
            CancellationToken.None);
        var result = await ((IArchiveRecomputeExecutor)executor).ExecuteScopedRefreshAsync(
            A.Dummy<ArchiveSnapshot>(), A.Dummy<RollupArchiveSnapshot>(), now, now.AddHours(1), RtIdSet.Of(new[] { M2, M1 })!,
            CancellationToken.None);

        // One rtId: the classic per-entity recompute. Several: one unscoped recompute (one generation, not one per rtId).
        Assert.Equal(new OctoObjectId?[] { M1, null }, executor.Scopes);
        Assert.Equal(new RecomputeExecutionResult(7, 7), result);
    }

    /// <summary>An executor as written before AB#9016: it implements only <see cref="IArchiveRecomputeExecutor.ExecuteAsync"/>.</summary>
    private sealed class PreviousContractExecutor : IArchiveRecomputeExecutor
    {
        public List<OctoObjectId?> Scopes { get; } = new();

        public Task<RecomputeExecutionResult> ExecuteAsync(
            ArchiveSnapshot source, RollupArchiveSnapshot rollup, DateTime rangeStart, DateTime rangeEnd,
            OctoObjectId? rtIdScope, CancellationToken cancellationToken)
        {
            Scopes.Add(rtIdScope);
            return Task.FromResult(new RecomputeExecutionResult(7, 7));
        }
    }
}
