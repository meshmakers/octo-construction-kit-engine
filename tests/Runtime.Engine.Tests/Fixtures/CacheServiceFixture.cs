using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    public string TenantId { get; } = Guid.NewGuid().ToString();


    public async Task<ICkCacheService> GetCacheServiceAsync()
    {
        var serviceProvider = Services.BuildServiceProvider();
        var ckModelFilePath = "sampleData/CkTest/ConstructionKit/ck-test.cache.json";

        var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
        ckCacheService.CreateTenant(TenantId);
        await ckCacheService.RestoreCacheAsync(TenantId, File.OpenRead(ckModelFilePath));

        return ckCacheService;
    }
}