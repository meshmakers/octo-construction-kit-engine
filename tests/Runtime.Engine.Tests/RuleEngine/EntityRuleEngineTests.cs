using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RuleEngine;

public class EntityRuleEngineTests: IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public EntityRuleEngineTests(TemporaryDirectoryFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public async Task Test1()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var temporaryDirectory = _fixture.CreateTempDirectory();
            
            var ckCacheService = serviceProvider.GetRequiredService<ICkCacheService>();
            var localRepository = new LocalDirectoryRepository("test", temporaryDirectory);
            
            
            var ruleEngine = new EntityRuleEngine(ckCacheService, localRepository);
            
            // var r = await ruleEngine.ValidateAsync(new[]
            // {
            //     // new EntityUpdateInfo(new RtEntity()
            //     // {
            //     //     CkTypeId = "Test/test",
            //     //     Attributes = { }
            //     //
            //     // })
            // });
        }
    }
}