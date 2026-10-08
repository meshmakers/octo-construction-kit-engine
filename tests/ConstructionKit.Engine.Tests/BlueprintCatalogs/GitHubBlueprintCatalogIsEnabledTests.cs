using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// AB#6112: the GitHub blueprint catalogs can be switched off by configuration (production must not
/// read the private main-lane catalog). The default keeps the pre-switch behaviour (enabled).
/// </summary>
public sealed class GitHubBlueprintCatalogIsEnabledTests
{
    private static PrivateGitHubBlueprintCatalog Private(bool? isEnabled, IHttpClientFactory? http = null)
    {
        var options = new PrivateGitHubBlueprintCatalogOptions
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), nameof(GitHubBlueprintCatalogIsEnabledTests),
                Guid.NewGuid().ToString("N"))
        };
        if (isEnabled.HasValue)
        {
            options.IsEnabled = isEnabled.Value;
        }

        return new PrivateGitHubBlueprintCatalog(A.Fake<IBlueprintSerializer>(), http ?? A.Fake<IHttpClientFactory>(),
            A.Fake<IGitHubClientFactory>(), Options.Create(options));
    }

    [Fact]
    public void Default_IsEnabled_KeepsReadAndWrite()
    {
        Assert.True(new PrivateGitHubBlueprintCatalogOptions().IsEnabled);
        Assert.True(new PublicGitHubBlueprintCatalogOptions().IsEnabled);

        var catalog = Private(isEnabled: null);

        Assert.True(catalog.CanRead);
        Assert.True(catalog.CanWrite);
    }

    [Fact]
    public void Disabled_CannotReadOrWrite()
    {
        var catalog = Private(isEnabled: false);

        Assert.False(catalog.CanRead);
        Assert.False(catalog.CanWrite);
    }

    [Fact]
    public void PublicCatalog_Disabled_CannotRead()
    {
        var catalog = new PublicGitHubBlueprintCatalog(A.Fake<IBlueprintSerializer>(), A.Fake<IHttpClientFactory>(),
            A.Fake<IGitHubClientFactory>(), Options.Create(new PublicGitHubBlueprintCatalogOptions { IsEnabled = false }));

        Assert.False(catalog.CanRead);
    }

    [Fact]
    public async Task Manager_DisabledPrivateCatalog_IsSkippedForListingResolutionAndRefresh()
    {
        var http = A.Fake<IHttpClientFactory>();
        var disabled = Private(isEnabled: false, http);
        var manager = new BlueprintCatalogManager(NullLogger<BlueprintCatalogManager>.Instance, [disabled]);

        var list = await manager.ListAsync(0, 100);
        var range = await manager.IsExistingAsync(new BlueprintIdVersionRange("EnergyCommunity.Base-[2.0,3.0)"));
        var refresh = await manager.RefreshAllCatalogCachesAsync();

        Assert.Empty(list.Items);
        Assert.False(range.Exists);
        Assert.Equal(BlueprintCatalogRefreshStatus.Skipped, Assert.Single(refresh).Status);
        A.CallTo(() => http.CreateClient(A<Uri>._)).MustNotHaveHappened();
    }
}
