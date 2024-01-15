using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    private readonly ServiceProvider _serviceProvider;

    public CacheServiceFixture()
    {
        RepositoryPath = CreateTempDirectory();

        _serviceProvider = Services.BuildServiceProvider();
    }

    public string RepositoryPath { get; }
    public string TenantId { get; } = Guid.NewGuid().ToString();

    public override void Dispose()
    {
        base.Dispose();

        _serviceProvider.Dispose();
    }

    public async Task<ICkCacheService> GetCacheServiceAsync()
    {
        var ckModelFilePath = "ck-test.cache.json";

        var ckCacheService = _serviceProvider.GetRequiredService<ICkCacheService>();
        ckCacheService.CreateTenant(TenantId);
        await ckCacheService.RestoreCacheAsync(TenantId, File.OpenRead(ckModelFilePath));

        return ckCacheService;
    }

    public IRtRepositorySerializer GetRtRepositorySerializer()
    {
        var rtSerializer = _serviceProvider.GetRequiredService<IRtRepositorySerializer>();
        return rtSerializer;
    }

    public IBulkRtMutation GetBulkRtMutation()
    {
        var bulkRtMutation = _serviceProvider.GetRequiredService<IBulkRtMutation>();
        return bulkRtMutation;
    }
}