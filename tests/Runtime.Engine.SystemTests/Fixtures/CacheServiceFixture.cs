using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.SystemTests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    public string RepositoryPath { get; }
    public string TenantId { get; } = Guid.NewGuid().ToString();

    public CacheServiceFixture()
    {
        RepositoryPath = CreateTempDirectory();
    }

    public async Task<ICkCacheService> GetCacheServiceAsync()
    {
        await using (var serviceProvider = Services.BuildServiceProvider())
        {
            var ckModelFilePath = "sampleData/CkTest/ConstructionKit/ck-test.cache.json";

            var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
            ckCacheService.CreateTenant(TenantId);
            await ckCacheService.RestoreCacheAsync(TenantId, File.OpenRead(ckModelFilePath));

            return ckCacheService;
        }
    }
    
    public async Task<IRtSerializer> GetRtSerializerAsync()
    {
        await using (var serviceProvider = Services.BuildServiceProvider())
        {
            var rtSerializer = serviceProvider.GetRequiredService<IRtSerializer>();
            return rtSerializer;
        }
    }
}