using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RuleEngine;

public class EntityRuleEngineTests : IClassFixture<CacheServiceFixture>
{
    private readonly CacheServiceFixture _fixture;

    public EntityRuleEngineTests(CacheServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ValidateAsync_NonExistingTypeId_Fail()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(new RtEntity
            {
                CkTypeId = "Sample1/SampleType35",
            }, EntityModOptions.Create)
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToCreate);
        Assert.Empty(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(3, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_MandatoryAttributeWithoutDefaultMissing_Fail()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(new RtEntity
            {
                CkTypeId = "Test/City"
            }, EntityModOptions.Create)
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToCreate);
        Assert.Empty(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(2, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_AbstractType_Fail()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(new RtEntity
            {
                CkTypeId = "Test/LocationWithSensor"
            }, EntityModOptions.Create)
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToCreate);
        Assert.Empty(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(4, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public async Task ValidateAsync_Create_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" }
                    }), EntityModOptions.Create)
        }, operationResult);

        Assert.Single(ruleEngineResult.RtEntitiesToCreate);
        Assert.Empty(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.Empty(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
    
    [Fact]
    public async Task ValidateAsync_Update_MissingMandatoryAttribute_Fail()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", null }
                    }), EntityModOptions.Update)
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToCreate);
        Assert.Empty(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.Single(operationResult.Messages);
        Assert.False(operationResult.HasErrors);
        Assert.True(operationResult.HasFatalErrors);
        Assert.Equal(5, operationResult.Messages[0].MessageNumber);
    }
    
    [Fact]
    public async Task ValidateAsync_Update_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            new EntityUpdateInfo(
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" }
                    }), EntityModOptions.Update)
        }, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Empty(ruleEngineResult.RtEntitiesToCreate);
        Assert.Single(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }
}