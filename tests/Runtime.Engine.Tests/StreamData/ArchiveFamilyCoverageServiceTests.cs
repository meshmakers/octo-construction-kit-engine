using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157: family coverage — the queried archive plus every transitively reachable rollup, each
/// with its bucket geometry, declared functions and measured coverage.
/// </summary>
public class ArchiveFamilyCoverageServiceTests
{
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("EnergyMeasurement"));
    private static readonly DateTime From = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IArchiveRuntimeStore _archiveStore = A.Fake<IArchiveRuntimeStore>();
    private readonly IRollupArchiveRuntimeStore _rollupStore = A.Fake<IRollupArchiveRuntimeStore>();
    private readonly IRollupDependencyGraph _graph = A.Fake<IRollupDependencyGraph>();
    private readonly IArchiveCoverageProvider _coverageProvider = A.Fake<IArchiveCoverageProvider>();

    public ArchiveFamilyCoverageServiceTests()
    {
        // A base archive has no rollup view; an unconfigured fake would hand out a dummy snapshot
        // and every rung would be reported as a rollup.
        A.CallTo(() => _rollupStore.GetAsync(A<OctoObjectId>._)).Returns((RollupArchiveSnapshot?)null);
    }

    private ArchiveFamilyCoverageService NewSut() =>
        new(_archiveStore, _rollupStore, _graph, _coverageProvider);

    private static ArchiveSnapshot BaseArchive(string? name = "raw", TimeSpan? period = null) =>
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, name,
            Array.Empty<CkArchiveColumnSpec>())
        {
            IsTimeRange = period is not null,
            Period = period,
        };

    private static RollupArchiveSnapshot RollupOver(
        IReadOnlyList<RollupSourceReference> sources,
        TimeSpan bucketSize,
        string? name = null,
        BucketAlignment alignment = BucketAlignment.FixedSize,
        params CkRollupFunction[] functions) =>
        new(OctoObjectId.GenerateNewId(), TargetType, CkArchiveStatus.Activated, name, sources,
            bucketSize, TimeSpan.FromMinutes(5), null,
            (functions.Length == 0 ? new[] { CkRollupFunction.Avg } : functions)
                .Select(f => new CkRollupAggregationSpec("voltage", f, null)).ToList(),
            null)
        { BucketAlignment = alignment };

    private static RollupArchiveSnapshot RollupOver(OctoObjectId sourceRtId, TimeSpan bucketSize, string? name = null) =>
        RollupOver(new[] { new RollupSourceReference(sourceRtId) }, bucketSize, name);

    private void StubArchive(ArchiveSnapshot archive) =>
        A.CallTo(() => _archiveStore.GetAsync(archive.RtId)).Returns(archive);

    private void StubRollupView(RollupArchiveSnapshot rollup) =>
        A.CallTo(() => _rollupStore.GetAsync(rollup.RtId)).Returns(rollup);

    private void StubFamily(OctoObjectId rootRtId, params RollupArchiveSnapshot[] dependents) =>
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(rootRtId))
            .Returns((IReadOnlyList<RollupArchiveSnapshot>)dependents);

    private void StubCoverage(OctoObjectId rtId, ArchiveCoverage? coverage) =>
        A.CallTo(() => _coverageProvider.GetCoverageAsync(rtId, A<CancellationToken>._)).Returns(coverage);

    // TC-COV-12: the family is exactly the set transitively reachable from the queried archive.
    [Fact]
    public async Task FamilyOfABaseArchive_ListsTheBaseFirstThenEveryReachableRollup()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var r1 = RollupOver(b.RtId, TimeSpan.FromHours(1), "hourly");
        var r2 = RollupOver(r1.RtId, TimeSpan.FromDays(1), "daily");
        var r3 = RollupOver(r2.RtId, TimeSpan.FromDays(30), "monthly");
        StubArchive(b);
        StubFamily(b.RtId, r1, r2, r3);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.Equal(4, rungs.Count);
        Assert.Equal(b.RtId, rungs[0].ArchiveRtId);
        Assert.True(rungs[0].IsBase);
        Assert.Equal(new[] { r1.RtId, r2.RtId, r3.RtId }, rungs.Skip(1).Select(r => r.ArchiveRtId));
        Assert.All(rungs.Skip(1), r => Assert.False(r.IsBase));
    }

    [Fact]
    public async Task FamilyOfABaseArchive_DoesNotListAnUnrelatedFamily()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var r1 = RollupOver(b.RtId, TimeSpan.FromHours(1), "hourly");
        var unrelatedBase = BaseArchive("base-c", TimeSpan.FromMinutes(15));
        var r4 = RollupOver(unrelatedBase.RtId, TimeSpan.FromHours(1), "unrelated-hourly");
        StubArchive(b);
        StubFamily(b.RtId, r1); // the graph never reaches the unrelated family

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(rungs, r => r.ArchiveRtId == r4.RtId);
        Assert.DoesNotContain(rungs, r => r.ArchiveRtId == unrelatedBase.RtId);
    }

    // TC-X-COV-04: a rung reachable through two paths inside the same family is listed once.
    [Fact]
    public async Task RungReachableThroughTwoSourcesOfTheSameFamily_IsListedOnce()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var p = RollupOver(b.RtId, TimeSpan.FromHours(1), "hourly");
        var multiSource = RollupOver(
            new[]
            {
                new RollupSourceReference(b.RtId, ValidTo: To),
                new RollupSourceReference(p.RtId, ValidFrom: To),
            },
            TimeSpan.FromDays(1), "daily");
        StubArchive(b);
        // The graph deduplicates the two paths (B → R directly and B → P → R).
        StubFamily(b.RtId, p, multiSource);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.Single(rungs, r => r.ArchiveRtId == multiSource.RtId);
        Assert.Equal(3, rungs.Count);
    }

    [Fact]
    public async Task TimeRangeBaseRung_ReportsItsPeriodAsBucketSizeAndNoStoredFunctions()
    {
        var b = BaseArchive("quarter-hour", TimeSpan.FromMinutes(15));
        StubArchive(b);
        StubFamily(b.RtId);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        var rung = Assert.Single(rungs);
        Assert.True(rung.IsBase);
        Assert.Equal("quarter-hour", rung.RtWellKnownName);
        Assert.Equal(CkArchiveStatus.Activated, rung.Status);
        Assert.Equal((long)TimeSpan.FromMinutes(15).TotalMilliseconds, rung.BucketSizeMs);
        Assert.Equal(BucketAlignment.FixedSize, rung.Alignment);
        Assert.Empty(rung.StoredFunctions);
    }

    [Fact]
    public async Task RawBaseRung_ReportsNoBucketSize()
    {
        var b = BaseArchive();
        StubArchive(b);
        StubFamily(b.RtId);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(rungs).BucketSizeMs);
    }

    [Fact]
    public async Task RollupRung_ReportsBucketSizeAlignmentAndItsDistinctDeclaredFunctions()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var quarterly = RollupOver(
            new[] { new RollupSourceReference(b.RtId) }, TimeSpan.FromDays(90), "quarterly",
            BucketAlignment.CalendarQuarter,
            CkRollupFunction.Sum, CkRollupFunction.Avg, CkRollupFunction.Sum);
        StubArchive(b);
        StubFamily(b.RtId, quarterly);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        var rung = rungs[1];
        Assert.False(rung.IsBase);
        Assert.Equal("quarterly", rung.RtWellKnownName);
        Assert.Equal((long)TimeSpan.FromDays(90).TotalMilliseconds, rung.BucketSizeMs);
        Assert.Equal(BucketAlignment.CalendarQuarter, rung.Alignment);
        Assert.Equal(new[] { CkRollupFunction.Sum, CkRollupFunction.Avg }, rung.StoredFunctions);
    }

    [Fact]
    public async Task RungWithMeasuredCoverage_ReportsAvailableFromAndTo()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        StubArchive(b);
        StubFamily(b.RtId);
        StubCoverage(b.RtId, new ArchiveCoverage(From, To));

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.Equal(From, rungs[0].AvailableFrom);
        Assert.Equal(To, rungs[0].AvailableTo);
    }

    // A rung that holds no data reports null/null — never a sentinel range.
    [Fact]
    public async Task RungWithoutCoverage_ReportsNullAvailableFromAndTo()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var empty = RollupOver(b.RtId, TimeSpan.FromHours(1), "never-populated");
        StubArchive(b);
        StubFamily(b.RtId, empty);
        StubCoverage(b.RtId, new ArchiveCoverage(From, To));
        StubCoverage(empty.RtId, null);

        var rungs = await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        Assert.Null(rungs[1].AvailableFrom);
        Assert.Null(rungs[1].AvailableTo);
    }

    [Fact]
    public async Task CoverageProvider_IsAskedExactlyOncePerRung()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var r1 = RollupOver(b.RtId, TimeSpan.FromHours(1), "hourly");
        var r2 = RollupOver(r1.RtId, TimeSpan.FromDays(1), "daily");
        StubArchive(b);
        StubFamily(b.RtId, r1, r2);

        await NewSut().GetFamilyCoverageAsync(b.RtId, TestContext.Current.CancellationToken);

        A.CallTo(() => _coverageProvider.GetCoverageAsync(b.RtId, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _coverageProvider.GetCoverageAsync(r1.RtId, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => _coverageProvider.GetCoverageAsync(r2.RtId, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    // The family may be keyed by any archive of it, including a rollup rung.
    [Fact]
    public async Task QueriedArchiveThatIsItselfARollup_IsReportedWithItsRollupFacts()
    {
        var b = BaseArchive("base-b", TimeSpan.FromMinutes(15));
        var hourly = RollupOver(b.RtId, TimeSpan.FromHours(1), "hourly");
        var daily = RollupOver(hourly.RtId, TimeSpan.FromDays(1), "daily");
        // The rollup is also visible through the archive store (its base view).
        A.CallTo(() => _archiveStore.GetAsync(hourly.RtId)).Returns(
            new ArchiveSnapshot(hourly.RtId, TargetType, CkArchiveStatus.Activated, "hourly",
                Array.Empty<CkArchiveColumnSpec>()));
        StubRollupView(hourly);
        StubFamily(hourly.RtId, daily);

        var rungs = await NewSut().GetFamilyCoverageAsync(hourly.RtId, TestContext.Current.CancellationToken);

        Assert.Equal(2, rungs.Count);
        Assert.Equal(hourly.RtId, rungs[0].ArchiveRtId);
        Assert.False(rungs[0].IsBase);
        Assert.Equal((long)TimeSpan.FromHours(1).TotalMilliseconds, rungs[0].BucketSizeMs);
        Assert.Equal(daily.RtId, rungs[1].ArchiveRtId);
    }

    [Fact]
    public async Task UnknownArchive_YieldsAnEmptyListWithoutAskingTheGraph()
    {
        var unknown = OctoObjectId.GenerateNewId();
        A.CallTo(() => _archiveStore.GetAsync(unknown)).Returns((ArchiveSnapshot?)null);

        var rungs = await NewSut().GetFamilyCoverageAsync(unknown, TestContext.Current.CancellationToken);

        Assert.Empty(rungs);
        A.CallTo(() => _graph.GetTransitiveDependentsAsync(A<OctoObjectId>._)).MustNotHaveHappened();
    }
}
