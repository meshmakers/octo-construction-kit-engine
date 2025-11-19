using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    public string TenantId { get; } = Guid.NewGuid().ToString();


    public async Task<ICkCacheService> GetCacheServiceAsync()
    {
        var serviceProvider = Services.BuildServiceProvider();
#if DEBUGL
        var ckModelFilePath = "../../../../TestCkModel/obj/DebugL/net10.0/octo-ck-cache/TestCkModel/out/ck-test.cache.json";
#elif DEBUG        
        var ckModelFilePath = "../../../../TestCkModel/obj/Debug/net10.0/octo-ck-cache/TestCkModel/out/ck-test.cache.json";
#else
        var ckModelFilePath = "../../../../TestCkModel/obj/Release/net10.0/octo-ck-cache/TestCkModel/out/ck-test.cache.json";
#endif

        var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
        ckCacheService.CreateTenant(TenantId);
        await ckCacheService.RestoreCacheAsync(TenantId, File.OpenRead(ckModelFilePath));

        return ckCacheService;
    }
}