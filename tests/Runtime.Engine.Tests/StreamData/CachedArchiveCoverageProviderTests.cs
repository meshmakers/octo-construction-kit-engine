using System;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157: the process-wide coverage memo (<see cref="ArchiveCoverageCache"/>) and the
/// tenant-scoped provider on top of it. The cache clock is injected, so freshness is asserted by
/// moving the clock, never by sleeping.
/// </summary>
public class CachedArchiveCoverageProviderTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private static readonly OctoObjectId ArchiveRt = OctoObjectId.GenerateNewId();
    private static readonly DateTime Start = new(2026, 5, 11, 14, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly IStreamDataRepository _repository = A.Fake<IStreamDataRepository>();
    private DateTime _now = Start;

    private ArchiveCoverageCache NewCache(TimeSpan? ttl = null) => new(ttl ?? Ttl, () => _now);

    private CachedArchiveCoverageProvider NewProvider(ArchiveCoverageCache cache, string tenantId = TenantA) =>
        new(tenantId, _repository, cache);

    private static ArchiveCoverage Coverage(int day) =>
        new(new DateTime(2025, 1, day, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));

    private void StubCoverage(ArchiveCoverage? coverage) =>
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._)).Returns(coverage);

    // TC-COV-08: a second request inside the freshness window is served from the memo.
    [Fact]
    public async Task SecondCallWithinTheTtl_IsServedFromTheCacheWithoutHittingTheRepository()
    {
        StubCoverage(Coverage(1));
        var provider = NewProvider(NewCache());

        var first = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        _now += TimeSpan.FromSeconds(59);
        var second = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // TC-COV-09: once the caching interval has elapsed the coverage is measured again.
    [Fact]
    public async Task CallAfterTheTtlHasElapsed_RefetchesFromTheRepository()
    {
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .Returns(Coverage(1)).Once()
            .Then.Returns(Coverage(2));
        var provider = NewProvider(NewCache());

        var first = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        _now += Ttl; // an entry aged exactly the TTL is stale
        var second = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Equal(Coverage(1), first);
        Assert.Equal(Coverage(2), second);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    // "No coverage" is the normal state of a fresh archive and just as expensive to measure.
    [Fact]
    public async Task NullCoverage_IsCachedLikeAnyOtherAnswer()
    {
        StubCoverage(null);
        var provider = NewProvider(NewCache());

        var first = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        var second = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Null(second);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // TC-COV-10: the same archive rtId in two tenants is memoised independently.
    [Fact]
    public async Task SameArchiveIdInTwoTenants_IsCachedIndependently()
    {
        var cache = NewCache();
        var repositoryB = A.Fake<IStreamDataRepository>();
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._)).Returns(Coverage(1));
        A.CallTo(() => repositoryB.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._)).Returns(Coverage(2));
        var providerA = NewProvider(cache);
        var providerB = new CachedArchiveCoverageProvider(TenantB, repositoryB, cache);

        var coverageA = await providerA.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        var coverageB = await providerB.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Equal(Coverage(1), coverageA);
        Assert.Equal(Coverage(2), coverageB);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => repositoryB.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RepositoryFailure_PropagatesAndIsNotCached()
    {
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .Throws(new InvalidOperationException("crate unreachable")).Once()
            .Then.Returns(Coverage(1));
        var provider = NewProvider(NewCache());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken));

        // The next request measures again instead of replaying the failure.
        var recovered = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Equal(Coverage(1), recovered);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task InvalidateForOneArchive_ForcesTheNextRequestToRefetch()
    {
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .Returns(Coverage(1)).Once()
            .Then.Returns(Coverage(2));
        var cache = NewCache();
        var provider = NewProvider(cache);
        await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        cache.Invalidate(TenantA, ArchiveRt);
        var afterInvalidate = await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        Assert.Equal(Coverage(2), afterInvalidate);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task InvalidateWithoutAnArchive_DropsEveryEntryOfThatTenantOnly()
    {
        var cache = NewCache();
        var otherArchive = OctoObjectId.GenerateNewId();
        var repositoryB = A.Fake<IStreamDataRepository>();
        A.CallTo(() => _repository.GetArchiveCoverageAsync(A<OctoObjectId>._, A<CancellationToken>._)).Returns(Coverage(1));
        A.CallTo(() => repositoryB.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._)).Returns(Coverage(2));
        var providerA = NewProvider(cache);
        var providerB = new CachedArchiveCoverageProvider(TenantB, repositoryB, cache);
        await providerA.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        await providerA.GetCoverageAsync(otherArchive, TestContext.Current.CancellationToken);
        await providerB.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        cache.Invalidate(TenantA);
        await providerA.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        await providerA.GetCoverageAsync(otherArchive, TestContext.Current.CancellationToken);
        await providerB.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        // Both of tenant A's archives were re-measured…
        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
        A.CallTo(() => _repository.GetArchiveCoverageAsync(otherArchive, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
        // …while tenant B's entry survived.
        A.CallTo(() => repositoryB.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ZeroTtl_MeasuresOnEveryRequest()
    {
        StubCoverage(Coverage(1));
        var provider = NewProvider(NewCache(TimeSpan.Zero));

        await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);
        await provider.GetCoverageAsync(ArchiveRt, TestContext.Current.CancellationToken);

        A.CallTo(() => _repository.GetArchiveCoverageAsync(ArchiveRt, A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public void NegativeTtl_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveCoverageCache(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void DefaultCacheTtl_IsSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), ArchiveCoverageCache.DefaultCacheTtl);
    }
}
