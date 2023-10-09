using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

public class CacheServiceFixture : TemporaryDirectoryFixture
{
    private readonly string _repositoryPath;
    public string TenantId { get; } = Guid.NewGuid().ToString();
    public ILocalRuntimeRepository LocalRepository { get; }

    public CacheServiceFixture()
    {
        _repositoryPath = CreateTempDirectory();
        LocalRepository = new LocalDirectoryRepository(TenantId, _repositoryPath);
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
}