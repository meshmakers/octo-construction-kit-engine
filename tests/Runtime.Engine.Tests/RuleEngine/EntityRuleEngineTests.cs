using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using TestCkModel.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RuleEngine;

public class EntityRuleEngineTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    [Fact]
    public async Task ValidateAsync_NonExistingTypeId_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Sample1/SampleType35"
            })
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Test/City"
            })
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity
            {
                CkTypeId = "Test/LocationWithSensor"
            })
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" },
                        { "RecordArrayTests", new List<object>() }
                    }))
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateUpdate(
                new RtEntityId("Test/Country", OctoObjectId.GenerateNewId()),
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", null }
                    }))
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateUpdate(
                new RtEntityId("Test/Country", OctoObjectId.GenerateNewId()),
                new RtEntity(
                    "Test/Country",
                    OctoObjectId.GenerateNewId(),
                    new Dictionary<string, object?>
                    {
                        { "Designation", "Test" }
                    }))
        ], originFileResolver, operationResult);

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
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<object>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        ], originFileResolver, operationResult);

        var list = rtEntity.GetAttributeStringValues("StringArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Single(list);
        Assert.Equal("a", list[0]);
    }

    [Fact]
    public async Task ValidateAsync_IntArrayWithDefaultValues_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<object>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        ], originFileResolver, operationResult);

        var list = rtEntity.GetAttributeValues<int>("IntArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Single(list);
        Assert.Equal(6, list[0]);
    }

    [Fact]
    public async Task ValidateAsync_RecordArray_Empty_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);
        var rtEntity = new RtEntity(
            "Test/Country",
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                { "Designation", "Test" },
                { "RecordArrayTests", new List<RtRecord>() }
            });

        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity)
        ], originFileResolver, operationResult);

        var list = rtEntity.GetRtRecordAttributeValues<RtTestRecordRecord>("RecordArrayTests");

        Assert.Empty(operationResult.Messages);
        Assert.Single(ruleEngineResult.RtEntitiesToInsert);
        Assert.Empty(list);
    }
}