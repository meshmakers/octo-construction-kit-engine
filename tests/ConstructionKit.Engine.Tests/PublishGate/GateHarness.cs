using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.PublishGate;

/// <summary>
///     The real engine services (gate, resolver, verdict service, catalog service) over a temp local catalog, a
///     <see cref="StubCatalog" /> and, optionally, a repository-backed GitHub catalog. No network.
/// </summary>
internal sealed class GateHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    public GateHarness(bool lane = false)
    {
        Root = Path.Combine(Path.GetTempPath(), $"PublishGate_{Guid.NewGuid():N}");
        LocalDir = Path.Combine(Root, "local");
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(Path.Combine(Root, "cache"));

        Stub.AllowsReplacingPublishedVersions = lane;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConstructionKit();
        services.Configure<LocalFileSystemCatalogOptions>(o =>
        {
            o.ApplyRootPath(LocalDir);
            o.IsEnabled = true;
        });
        services.Configure<PublicGitHubCatalogOptions>(o => o.IsEnabled = false);
        services.Configure<PrivateGitHubCatalogOptions>(o => o.IsEnabled = false);
        services.AddSingleton<ICatalog>(Stub);
        services.AddSingleton<ICatalog>(sp =>
            new RepositoryBackedCatalog(sp.GetRequiredService<ICkJsonSerializer>(), Repository,
                Path.Combine(Root, "cache"), lane));
        _provider = services.BuildServiceProvider();
    }

    public string Root { get; }
    public string LocalDir { get; }
    public StubCatalog Stub { get; } = new();
    public InMemoryGitHubRepository Repository { get; } = new();

    /// <summary>A new process: the index cache of the GitHub catalogs is gone and the next read has to rebuild it.</summary>
    public void ClearCatalogCache()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "cache")))
        {
            File.Delete(file);
        }
    }

    public CkPublishGate Gate => _provider.GetRequiredService<CkPublishGate>();
    public ICatalogService CatalogService => _provider.GetRequiredService<ICatalogService>();
    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public Task<CkPublishGateResult> EvaluateAsync(CkCompiledModelRoot model, string catalog = StubCatalog.Name) =>
        Gate.EvaluateAsync(catalog, model);

    /// <summary>Publishes through the real catalog service (the path of <c>ckc Publish</c> and <c>CkCompile</c>).</summary>
    public Task PublishAsync(CkCompiledModelRoot model, string catalog = StubCatalog.Name, bool force = false) =>
        CatalogService.PublishAsync(catalog, model, new OriginFileResolver("ck-test.json"), force);

    /// <summary>Writes the model straight into the catalog, past the gate (a version published earlier).</summary>
    public async Task SeedAsync(CkCompiledModelRoot model, string catalog = StubCatalog.Name)
    {
        if (catalog == StubCatalog.Name)
        {
            Stub.Seed(model);
            return;
        }

        var target = _provider.GetServices<ICatalog>().Single(c => c.CatalogName == catalog);
        await target.PublishAsync(model, true);
    }

    public static string[] ErrorCodes(CkPublishGateResult result) =>
        result.Messages.Where(m => m.Severity == CkCompileGateSeverity.Error).Select(m => m.Code ?? "").ToArray();

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }
}
