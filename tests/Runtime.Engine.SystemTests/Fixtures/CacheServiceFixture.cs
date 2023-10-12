using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    private readonly ServiceProvider _serviceProvider;

    public string RepositoryPath { get; }
    public string TenantId { get; } = Guid.NewGuid().ToString();

    public CacheServiceFixture()
    {
        RepositoryPath = CreateTempDirectory();

        _serviceProvider = Services.BuildServiceProvider();
    }

    public override void Dispose()
    {
        base.Dispose();

        _serviceProvider.Dispose();
    }

    public async Task<ICkCacheService> GetCacheServiceAsync()
    {
        var ckModelFilePath = "sampleData/CkTest/ConstructionKit/ck-test.cache.json";

        var ckCacheService = _serviceProvider.GetRequiredService<ICkCacheService>();
        ckCacheService.CreateTenant(TenantId);
        await ckCacheService.RestoreCacheAsync(TenantId, File.OpenRead(ckModelFilePath));

        return ckCacheService;
    }

    public IRtSerializer GetRtSerializer()
    {
        var rtSerializer = _serviceProvider.GetRequiredService<IRtSerializer>();
        return rtSerializer;
    }

    public IBulkRtMutation GetBulkRtMutation()
    {
        var bulkRtMutation = _serviceProvider.GetRequiredService<IBulkRtMutation>();
        return bulkRtMutation;
    }
}