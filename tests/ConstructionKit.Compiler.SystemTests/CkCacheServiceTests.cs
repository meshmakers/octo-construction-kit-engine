using Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.Fixtures;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests;

public class CkCacheServiceTests : IClassFixture<ServiceCollectionFixture>
{
    private readonly ServiceCollectionFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public CkCacheServiceTests(ServiceCollectionFixture fixture, ITestOutputHelper testOutputHelper)
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
}