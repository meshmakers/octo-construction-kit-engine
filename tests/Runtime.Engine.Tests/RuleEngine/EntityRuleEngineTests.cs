using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;

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
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Sample1/SampleType35"
            })
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToInsert);
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
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Test/City"
            })
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToInsert);
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
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Test/LocationWithSensor"
            })
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToInsert);
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
            EntityUpdateInfo<RtEntity>.CreateInsert(
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" },
                        { "RecordArrayTests", new List<object>() }
                    }))
        }, operationResult);

        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
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
            EntityUpdateInfo<RtEntity>.CreateUpdate(
                new RtEntityId("Test/Country", OctoObjectId.GenerateNewId()),
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", null }
                    }))
        }, operationResult);

        Assert.Empty(ruleEngineResult.RtEntitiesToInsert);
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
            EntityUpdateInfo<RtEntity>.CreateUpdate(
                new RtEntityId("Test/Country", OctoObjectId.GenerateNewId()),
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" }
                    }))
        }, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Empty(ruleEngineResult.RtEntitiesToInsert);
        Assert.Single(ruleEngineResult.RtEntitiesToUpdate);
        Assert.Empty(ruleEngineResult.RtEntitiesToDelete);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task ValidateAsync_StringArrayWithDefaultValues_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<object>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        }, operationResult);

        var list = rtEntity.GetAttributeStringValues("StringArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Single(list);
        Assert.Equal("a", list[0]);
    }

    [Fact]
    public async Task ValidateAsync_IntArrayWithDefaultValues_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<object>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        }, operationResult);

        var list = rtEntity.GetAttributeValues<int>("IntArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Single(list);
        Assert.Equal(6, list[0]);
    }

    [Fact]
    public async Task ValidateAsync_RecordArray_Empty_OK()
    {
        var ckCacheService = await _fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<RtRecord>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(_fixture.TenantId, new[]
        {
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        }, operationResult);

        var list = rtEntity.GetRtRecordAttributeValues<RtTestRecordRecord>("RecordArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Empty(list);
    }
}