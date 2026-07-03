using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Tests for <see cref="BlueprintCatalogManager" /> routing (find-by-name + CanWrite gate) and for the
/// refresh resilience contract that <see cref="Commands" />-side read commands rely on.
/// </summary>
public class BlueprintCatalogManagerTests
{
    private static IBlueprintCatalog FakeCatalog(string name, bool canWrite = true, bool canRead = true)
    {
        var catalog = A.Fake<IBlueprintCatalog>();
        A.CallTo(() => catalog.CatalogName).Returns(name);
        A.CallTo(() => catalog.CanWrite).Returns(canWrite);
        A.CallTo(() => catalog.CanRead).Returns(canRead);
        A.CallTo(() => catalog.IsSupportingSourceIdentifier(A<object?>._)).Returns(true);
        return catalog;
    }

    private static BlueprintCatalogManager Manager(params IBlueprintCatalog[] catalogs)
        => new(NullLogger<BlueprintCatalogManager>.Instance, catalogs);

    [Fact]
    public async Task UnpublishAsync_UnknownCatalog_ThrowsCatalogNotFound()
    {
        var manager = Manager(FakeCatalog("Known"));

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => manager.UnpublishAsync("Missing", new BlueprintId("X", "1.0.0")));
    }

    [Fact]
    public async Task UnpublishAsync_ReadOnlyCatalog_ThrowsAndDoesNotInvokeCatalog()
    {
        var readOnly = FakeCatalog("RO", canWrite: false);
        var manager = Manager(readOnly);

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => manager.UnpublishAsync("RO", new BlueprintId("X", "1.0.0")));

        A.CallTo(() => readOnly.UnpublishAsync(A<BlueprintId>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task UnpublishAsync_WritableCatalog_DelegatesToCatalog()
    {
        var writable = FakeCatalog("W");
        var manager = Manager(writable);
        var id = new BlueprintId("X", "1.0.0");

        await manager.UnpublishAsync("W", id);

        A.CallTo(() => writable.UnpublishAsync(id, A<object?>._, A<CancellationToken?>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UnpublishAllVersionsAsync_ReadOnlyCatalog_ThrowsAndDoesNotInvokeCatalog()
    {
        var readOnly = FakeCatalog("RO", canWrite: false);
        var manager = Manager(readOnly);

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => manager.UnpublishAllVersionsAsync("RO", "X"));

        A.CallTo(() => readOnly.UnpublishAllVersionsAsync(A<string>._, A<object?>._, A<CancellationToken?>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_OneCatalogThrows_StillRefreshesTheOthersAndReportsFailure()
    {
        var failing = FakeCatalog("Failing");
        A.CallTo(() => failing.RefreshCatalogAsync(A<object?>._, A<bool>._))
            .Throws(new InvalidOperationException("catalog is misconfigured"));
        var working = FakeCatalog("Working");

        var manager = Manager(failing, working);

        // Must not propagate the failing catalog's exception …
        var results = await manager.RefreshAllCatalogCachesAsync(force: true);

        // … and must still have refreshed the working catalog.
        A.CallTo(() => working.RefreshCatalogAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();

        // The failure is reported per catalog instead of being swallowed.
        Assert.Equal(2, results.Count);
        var failed = Assert.Single(results, r => r.CatalogName == "Failing");
        Assert.Equal(BlueprintCatalogRefreshStatus.Failed, failed.Status);
        Assert.Equal("catalog is misconfigured", failed.Message);
        var refreshed = Assert.Single(results, r => r.CatalogName == "Working");
        Assert.Equal(BlueprintCatalogRefreshStatus.Refreshed, refreshed.Status);
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_SkipsUnreadableCatalogAndReportsSkip()
    {
        var unreadable = FakeCatalog("Unreadable", canRead: false);
        var manager = Manager(unreadable);

        var results = await manager.RefreshAllCatalogCachesAsync(force: true);

        A.CallTo(() => unreadable.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustNotHaveHappened();
        var skipped = Assert.Single(results);
        Assert.Equal(BlueprintCatalogRefreshStatus.Skipped, skipped.Status);
        Assert.Equal("Unreadable", skipped.CatalogName);
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_ReturnsResultsOrderedByCatalogOrder()
    {
        var second = FakeCatalog("Second");
        A.CallTo(() => second.Order).Returns(20);
        var first = FakeCatalog("First");
        A.CallTo(() => first.Order).Returns(10);

        var manager = Manager(second, first);

        var results = await manager.RefreshAllCatalogCachesAsync();

        Assert.Equal(["First", "Second"], results.Select(r => r.CatalogName));
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_SkipsCatalogNotSupportingSourceIdentifier()
    {
        var unsupporting = FakeCatalog("Unsupporting");
        A.CallTo(() => unsupporting.IsSupportingSourceIdentifier(A<object?>._)).Returns(false);
        var manager = Manager(unsupporting);

        var results = await manager.RefreshAllCatalogCachesAsync(sourceIdentifier: new object());

        A.CallTo(() => unsupporting.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustNotHaveHappened();
        var skipped = Assert.Single(results);
        Assert.Equal(BlueprintCatalogRefreshStatus.Skipped, skipped.Status);
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_PassesSourceIdentifierThroughToCatalogs()
    {
        var catalog = FakeCatalog("Known");
        var manager = Manager(catalog);
        var sourceIdentifier = new object();

        await manager.RefreshAllCatalogCachesAsync(sourceIdentifier);

        A.CallTo(() => catalog.IsSupportingSourceIdentifier(sourceIdentifier)).MustHaveHappened();
        A.CallTo(() => catalog.RefreshCatalogAsync(sourceIdentifier, false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_KnownCatalog_RefreshesForcedAndReturnsRefreshed()
    {
        var catalog = FakeCatalog("Known");
        var manager = Manager(catalog);

        var result = await manager.RefreshCatalogCacheAsync("Known", force: true);

        A.CallTo(() => catalog.RefreshCatalogAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();
        Assert.Equal(BlueprintCatalogRefreshStatus.Refreshed, result.Status);
        Assert.Equal("Known", result.CatalogName);
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_RefreshesOnlyTheNamedCatalog()
    {
        var named = FakeCatalog("Named");
        var other = FakeCatalog("Other");
        var manager = Manager(named, other);

        var result = await manager.RefreshCatalogCacheAsync("Named");

        A.CallTo(() => named.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => other.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustNotHaveHappened();
        Assert.Equal("Named", result.CatalogName);
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_NameLookupIsCaseInsensitive()
    {
        var catalog = FakeCatalog("LocalFileSystemBlueprintCatalog");
        var manager = Manager(catalog);

        var result = await manager.RefreshCatalogCacheAsync("localfilesystemblueprintcatalog");

        A.CallTo(() => catalog.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustHaveHappenedOnceExactly();
        Assert.Equal("LocalFileSystemBlueprintCatalog", result.CatalogName);
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_UnknownCatalog_ThrowsCatalogNotFound()
    {
        var manager = Manager(FakeCatalog("Known"));

        var ex = await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => manager.RefreshCatalogCacheAsync("Missing"));

        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_UnreadableCatalog_ReturnsSkippedWithoutInvokingCatalog()
    {
        var unreadable = FakeCatalog("Unreadable", canRead: false);
        var manager = Manager(unreadable);

        var result = await manager.RefreshCatalogCacheAsync("Unreadable");

        A.CallTo(() => unreadable.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustNotHaveHappened();
        Assert.Equal(BlueprintCatalogRefreshStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task RefreshCatalogCacheAsync_CatalogThrows_ReturnsFailedWithMessage()
    {
        var failing = FakeCatalog("Failing");
        A.CallTo(() => failing.RefreshCatalogAsync(A<object?>._, A<bool>._))
            .Throws(new InvalidOperationException("remote unreachable"));
        var manager = Manager(failing);

        var result = await manager.RefreshCatalogCacheAsync("Failing");

        Assert.Equal(BlueprintCatalogRefreshStatus.Failed, result.Status);
        Assert.Equal("remote unreachable", result.Message);
    }
}
