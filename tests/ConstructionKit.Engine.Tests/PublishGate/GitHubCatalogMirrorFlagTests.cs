using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     AB#6580: the cache refresh of the GitHub catalog reads the Pages mirror. A mirror that answers 404 for an index that
///     the repository (or the root index) says exists must flag the cache as unreachable - it used to cache "empty" - so
///     a "model not found" answer of the cache is never mistaken for "model does not exist".
/// </summary>
public sealed class GitHubCatalogMirrorFlagTests : IDisposable
{
    private const string Catalog = RepositoryBackedCatalog.Name;
    private readonly GateHarness _h = new();

    public void Dispose() => _h.Dispose();

    private ICatalog CatalogUnderTest => _h.Get<IEnumerable<ICatalog>>().Single(c => c.CatalogName == Catalog);

    private async Task<bool> UnreachableAfterRefreshAsync()
    {
        await CatalogUnderTest.RefreshCatalogAsync(null, true);
        var answer = await CatalogUnderTest.IsExistingAsync(new CkModelIdVersionRange("GateModel", "[1.0.0,2.0.0)"));
        return answer.SourceUnreachable;
    }

    private Task PublishAsync() => _h.PublishAsync(GateModels.Create("1.0.0", "A,B", 2), Catalog);

    [Fact]
    public async Task A_mirror_that_serves_everything_is_reachable()
    {
        await PublishAsync();

        Assert.False(await UnreachableAfterRefreshAsync());
    }

    [Fact]
    public async Task A_mirror_404_for_the_root_index_that_the_repository_holds_is_unreachable_not_empty()
    {
        await PublishAsync();
        _h.Repository.PagesBehavior = path => path == "ck-models/v2/catalog.json" ? PagesAnswer.NotFound : PagesAnswer.Serve;
        // The v2 root has no model of this layout; give the repository one so that the mirror's 404 contradicts it.
        await _h.PublishAsync(GateModels.Create("1.0.0", "A,B", null, "ClassicModel"), Catalog);

        Assert.True(await UnreachableAfterRefreshAsync());
    }

    [Fact]
    public async Task A_mirror_404_for_the_root_index_of_a_repository_without_one_is_an_empty_catalog()
    {
        _h.Repository.PagesBehavior = _ => PagesAnswer.NotFound;

        Assert.False(await UnreachableAfterRefreshAsync());
    }

    [Fact]
    public async Task A_mirror_404_for_the_root_index_of_a_repository_that_does_not_exist_is_unreachable()
    {
        _h.Repository.State = GitHubRepositoryState.RepositoryNotFound;
        _h.Repository.PagesBehavior = _ => PagesAnswer.NotFound;

        Assert.True(await UnreachableAfterRefreshAsync());
    }

    [Fact]
    public async Task A_mirror_that_loses_the_model_index_the_root_lists_is_unreachable()
    {
        await PublishAsync();
        _h.Repository.PagesBehavior = path => path == "ck-models/v3/g/GateModel/catalog.json" ? PagesAnswer.ServerError : PagesAnswer.Serve;

        Assert.True(await UnreachableAfterRefreshAsync());
    }

    [Fact]
    public async Task A_mirror_that_loses_the_version_index_of_a_major_fails_the_refresh_loudly()
    {
        await PublishAsync();
        _h.Repository.PagesBehavior = path => path == "ck-models/v3/g/GateModel/1/catalog.json" ? PagesAnswer.NotFound : PagesAnswer.Serve;

        await Assert.ThrowsAsync<ModelCatalogException>(() => CatalogUnderTest.RefreshCatalogAsync(null, true));
    }
}
