using Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.Fixtures;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests;

public class CkCacheServiceTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public CkCacheServiceTests(TemporaryDirectoryFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task LoadCkModelAsync_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var operationResult = new OperationResult();
            var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
            ckCacheService.CreateTenant("test1");
            await ckCacheService.LoadCkModelAsync("test1", sampleData.sample1.Builder.Build(), operationResult);

            Assert.NotNull(ckCacheService.GetCkType("test1", "sample1/Demo1"));
        }
    }

    [Fact]
    public async Task LoadSaveAndRestore_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var tempDirectory = _fixture.CreateTempDirectory();
            var filePath = Path.Combine(tempDirectory, "test.cache.json");
            
            var operationResult = new OperationResult();
            var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
            ckCacheService.CreateTenant("test1");
            await ckCacheService.LoadCkModelAsync("test1", sampleData.sample1.Builder.Build(), operationResult);
            await using (var streamWriter = File.OpenWrite(filePath))
            {
                await ckCacheService.SaveCacheAsync("test1", streamWriter);
            }

            ckCacheService.CreateTenant("test2");
            await using var stream = File.OpenRead(filePath);
            await ckCacheService.RestoreCacheAsync("test2", stream);

            Assert.NotNull(ckCacheService.GetCkType("test2", "sample1/Demo1"));
        }
    }
}