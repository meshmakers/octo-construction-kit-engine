using System;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157 — the half-open validity-span semantics of <see cref="RollupSourceReference"/> and the
/// per-bucket source selection helpers on <see cref="RollupArchiveSnapshot"/>.
/// </summary>
public class RollupSourceReferenceTests
{
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly OctoObjectId LegacyRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId NativeRt = OctoObjectId.GenerateNewId();

    private static readonly DateTime Cutover = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static RollupArchiveSnapshot Rollup(params RollupSourceReference[] sources) =>
        new(
            OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, null,
            sources,
            TimeSpan.FromDays(1), TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);

    // ---- IsUnbounded ------------------------------------------------------------------------

    [Fact]
    public void IsUnbounded_NoBounds_True()
    {
        Assert.True(new RollupSourceReference(LegacyRt).IsUnbounded);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IsUnbounded_AnyBoundSet_False(bool hasFrom, bool hasTo)
    {
        var reference = new RollupSourceReference(
            LegacyRt,
            hasFrom ? Utc(2025, 1, 1) : null,
            hasTo ? Utc(2027, 1, 1) : null);

        Assert.False(reference.IsUnbounded);
    }

    // ---- Contains: ValidFrom inclusive, ValidTo exclusive -----------------------------------

    [Fact]
    public void Contains_BucketStartingExactlyAtValidFrom_IsCovered()
    {
        var reference = new RollupSourceReference(NativeRt, ValidFrom: Cutover);

        Assert.True(reference.Contains(Cutover, Cutover.AddDays(1)));
    }

    [Fact]
    public void Contains_BucketEndingExactlyAtValidTo_IsCovered()
    {
        var reference = new RollupSourceReference(LegacyRt, ValidTo: Cutover);

        Assert.True(reference.Contains(Cutover.AddDays(-1), Cutover));
    }

    [Fact]
    public void Contains_BucketStartingExactlyAtValidTo_IsNotCovered()
    {
        var reference = new RollupSourceReference(LegacyRt, ValidTo: Cutover);

        Assert.False(reference.Contains(Cutover, Cutover.AddDays(1)));
    }

    [Fact]
    public void Contains_BucketStraddlingAValidFrom_IsNotCovered()
    {
        // A bucket must never mix two sources: partial overlap is not coverage.
        var reference = new RollupSourceReference(NativeRt, ValidFrom: Cutover);

        Assert.False(reference.Contains(Cutover.AddHours(-12), Cutover.AddHours(12)));
    }

    [Fact]
    public void Contains_UnboundedReference_CoversEveryBucket()
    {
        var reference = new RollupSourceReference(LegacyRt);

        Assert.True(reference.Contains(DateTime.MinValue, DateTime.MaxValue));
    }

    // TC-VAL-02: abutting spans (ValidTo of A == ValidFrom of B) are disjoint, and every bucket
    // is covered by exactly one of them.
    [Fact]
    public void AbuttingSpans_AreDisjoint_AndCoverTheCutoverBucketExactlyOnce()
    {
        var legacy = new RollupSourceReference(LegacyRt, ValidTo: Cutover);
        var native = new RollupSourceReference(NativeRt, ValidFrom: Cutover);

        var cutoverBucketStart = Cutover;
        var cutoverBucketEnd = Cutover.AddDays(1);

        Assert.False(legacy.Contains(cutoverBucketStart, cutoverBucketEnd));
        Assert.True(native.Contains(cutoverBucketStart, cutoverBucketEnd));

        var previousBucketStart = Cutover.AddDays(-1);
        Assert.True(legacy.Contains(previousBucketStart, Cutover));
        Assert.False(native.Contains(previousBucketStart, Cutover));
    }

    // ---- Clip -------------------------------------------------------------------------------

    [Fact]
    public void Clip_UnboundedReference_ReturnsRangeUnchanged()
    {
        var reference = new RollupSourceReference(LegacyRt);

        var clipped = reference.Clip(Utc(2026, 5, 1), Utc(2026, 5, 2));

        Assert.Equal((Utc(2026, 5, 1), Utc(2026, 5, 2)), clipped);
    }

    [Fact]
    public void Clip_ClipsBothEnds()
    {
        var reference = new RollupSourceReference(LegacyRt, Utc(2026, 5, 10), Utc(2026, 5, 20));

        var clipped = reference.Clip(Utc(2026, 5, 1), Utc(2026, 5, 31));

        Assert.Equal((Utc(2026, 5, 10), Utc(2026, 5, 20)), clipped);
    }

    [Fact]
    public void Clip_RangeInsideSpan_IsUnchanged()
    {
        var reference = new RollupSourceReference(LegacyRt, Utc(2026, 5, 1), Utc(2026, 6, 1));

        var clipped = reference.Clip(Utc(2026, 5, 10), Utc(2026, 5, 20));

        Assert.Equal((Utc(2026, 5, 10), Utc(2026, 5, 20)), clipped);
    }

    [Fact]
    public void Clip_RangeEntirelyBeforeSpan_ReturnsNull()
    {
        var reference = new RollupSourceReference(NativeRt, ValidFrom: Cutover);

        Assert.Null(reference.Clip(Cutover.AddDays(-5), Cutover.AddDays(-1)));
    }

    [Fact]
    public void Clip_RangeEntirelyAfterSpan_ReturnsNull()
    {
        var reference = new RollupSourceReference(LegacyRt, ValidTo: Cutover);

        Assert.Null(reference.Clip(Cutover, Cutover.AddDays(5)));
    }

    [Fact]
    public void Clip_RangeEndingExactlyAtValidFrom_ReturnsNull()
    {
        // The intersection would be the empty half-open range [ValidFrom, ValidFrom).
        var reference = new RollupSourceReference(NativeRt, ValidFrom: Cutover);

        Assert.Null(reference.Clip(Cutover.AddDays(-1), Cutover));
    }

    // ---- Snapshot helpers -------------------------------------------------------------------

    [Fact]
    public void SingleUnboundedSourceRtId_ExactlyOneUnboundedSource_ReturnsIt()
    {
        var rollup = Rollup(new RollupSourceReference(LegacyRt));

        Assert.Equal(LegacyRt, rollup.SingleUnboundedSourceRtId);
    }

    [Fact]
    public void SingleUnboundedSourceRtId_SingleBoundedSource_ReturnsNull()
    {
        var rollup = Rollup(new RollupSourceReference(LegacyRt, ValidTo: Cutover));

        Assert.Null(rollup.SingleUnboundedSourceRtId);
    }

    [Fact]
    public void SingleUnboundedSourceRtId_TwoSources_ReturnsNull()
    {
        var rollup = Rollup(
            new RollupSourceReference(LegacyRt, ValidTo: Cutover),
            new RollupSourceReference(NativeRt, ValidFrom: Cutover));

        Assert.Null(rollup.SingleUnboundedSourceRtId);
    }

    [Fact]
    public void SingleUnboundedSourceRtId_NoSources_ReturnsNull()
    {
        Assert.Null(Rollup().SingleUnboundedSourceRtId);
    }

    [Fact]
    public void HasSource_MatchesRegardlessOfSpan()
    {
        var rollup = Rollup(
            new RollupSourceReference(LegacyRt, ValidTo: Cutover),
            new RollupSourceReference(NativeRt, ValidFrom: Cutover));

        Assert.True(rollup.HasSource(LegacyRt));
        Assert.True(rollup.HasSource(NativeRt));
        Assert.False(rollup.HasSource(OctoObjectId.GenerateNewId()));
    }

    [Fact]
    public void SourceForBucket_PicksLegacyBeforeAndNativeFromTheCutover()
    {
        var rollup = Rollup(
            new RollupSourceReference(LegacyRt, ValidTo: Cutover),
            new RollupSourceReference(NativeRt, ValidFrom: Cutover));

        Assert.Equal(LegacyRt, rollup.SourceForBucket(Cutover.AddDays(-1), Cutover)!.SourceArchiveRtId);
        Assert.Equal(NativeRt, rollup.SourceForBucket(Cutover, Cutover.AddDays(1))!.SourceArchiveRtId);
    }

    [Fact]
    public void SourceForBucket_BucketInsideAGap_ReturnsNull()
    {
        var gapStart = Cutover;
        var gapEnd = Cutover.AddDays(10);
        var rollup = Rollup(
            new RollupSourceReference(LegacyRt, ValidTo: gapStart),
            new RollupSourceReference(NativeRt, ValidFrom: gapEnd));

        Assert.Null(rollup.SourceForBucket(gapStart.AddDays(3), gapStart.AddDays(4)));
    }
}
