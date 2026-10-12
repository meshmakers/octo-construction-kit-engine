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
    private readonly bool _lane;
    private ServiceProvider _provider;

    /// <param name="lane">The dev lane catalog (published versions may be replaced).</param>
    /// <param name="repository">
    ///     An existing in-memory repository: a second harness on it is a second process (own services, own cache) that
    ///     publishes to the same repository (AB#6584).
    /// </param>
    public GateHarness(bool lane = false, InMemoryGitHubRepository? repository = null)
    {
        Repository = repository ?? new InMemoryGitHubRepository();
        Root = Path.Combine(Path.GetTempPath(), $"PublishGate_{Guid.NewGuid():N}");
        LocalDir = Path.Combine(Root, "local");
        Directory.CreateDirectory(LocalDir);
        Directory.CreateDirectory(Path.Combine(Root, "cache"));

        _lane = lane;
        Stub.AllowsReplacingPublishedVersions = lane;
        _provider = Build();
    }

    private ServiceProvider Build()
    {
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
                Path.Combine(Root, "cache"), _lane));
        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     A new process on the same repository and cache directory: the in-memory state of the services (model payloads read
    ///     earlier, singletons) is gone.
    /// </summary>
    public void Restart()
    {
        _provider.Dispose();
        _provider = Build();
    }

    public string Root { get; }
    public string LocalDir { get; }
    public StubCatalog Stub { get; } = new();
    public InMemoryGitHubRepository Repository { get; }

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
