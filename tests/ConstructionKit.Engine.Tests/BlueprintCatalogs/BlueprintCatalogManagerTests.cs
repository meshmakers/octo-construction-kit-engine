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
    public async Task RefreshAllCatalogCachesAsync_OneCatalogThrows_StillRefreshesTheOthers()
    {
        var failing = FakeCatalog("Failing");
        A.CallTo(() => failing.RefreshCatalogAsync(A<object?>._, A<bool>._))
            .Throws(new InvalidOperationException("catalog is misconfigured"));
        var working = FakeCatalog("Working");

        var manager = Manager(failing, working);

        // Must not propagate the failing catalog's exception …
        await manager.RefreshAllCatalogCachesAsync(force: true);

        // … and must still have refreshed the working catalog.
        A.CallTo(() => working.RefreshCatalogAsync(A<object?>._, true)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task RefreshAllCatalogCachesAsync_SkipsUnreadableCatalog()
    {
        var unreadable = FakeCatalog("Unreadable", canRead: false);
        var manager = Manager(unreadable);

        await manager.RefreshAllCatalogCachesAsync(force: true);

        A.CallTo(() => unreadable.RefreshCatalogAsync(A<object?>._, A<bool>._)).MustNotHaveHappened();
    }
}
