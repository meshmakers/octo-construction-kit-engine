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

    /// <summary>
    ///     AB#9753: the validation runs in parallel, but inserts and replaces come out in the order of the input, so an
    ///     auto-increment counter numbers a batch in batch order.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_InsertsAndReplaces_KeepTheInputOrder()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        RtEntity Country(int i) => new("Test/Country", OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { { "Designation", $"Country {i}" }, { "RecordArrayTests", new List<object>() } });

        var inserts = Enumerable.Range(0, 200).Select(Country).ToList();
        var replaces = Enumerable.Range(0, 200).Select(Country).ToList();
        var updateInfos = new List<IEntityUpdateInfo<RtEntity>>();
        for (var i = 0; i < inserts.Count; i++)
        {
            updateInfos.Add(EntityUpdateInfo<RtEntity>.CreateInsert(inserts[i]));
            updateInfos.Add(EntityUpdateInfo<RtEntity>.CreateReplace(
                new RtEntityId(new RtCkId<CkTypeId>("Test/Country"), replaces[i].RtId), replaces[i]));
        }

        var operationResult = new OperationResult();
        var ruleEngineResult = await new EntityRuleEngine(ckCacheService).ValidateAsync(fixture.TenantId, updateInfos,
            new OriginFileResolver("Test"), operationResult);

        Assert.False(operationResult.HasErrors || operationResult.HasFatalErrors, operationResult.GetMessages());
        Assert.Equal(inserts.Select(e => e.RtId), ruleEngineResult.RtEntitiesToInsert.Select(e => e.RtId));
        Assert.Equal(replaces.Select(e => e.RtId), ruleEngineResult.RtEntitiesToReplace.Values.Select(e => e.RtId));
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
        Assert.Empty(ruleEngineResult.UpdateGuards);
        Assert.False(operationResult.HasErrors);
        Assert.False(operationResult.HasFatalErrors);
    }

    [Fact]
    public async Task ValidateAsync_ConditionalUpdate_GuardFlowsToResult()
    {
        // The rule engine drops the EntityUpdateInfo wrapper and keeps only entity dicts
        // on its result. Pinning that the guard side-channel (UpdateGuards dictionary) is
        // populated for conditional updates and stays empty for regular ones — this is
        // what BulkRtMutation reads to decide between unguarded and guarded write paths.
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var operationResult = new OperationResult();
        var originFileResolver = new OriginFileResolver("Test");
        var ruleEngine = new EntityRuleEngine(ckCacheService);

        var conditionalId = new RtEntityId("Test/Country", OctoObjectId.GenerateNewId());
        var unguardedId = new RtEntityId("Test/Country", OctoObjectId.GenerateNewId());
        var newTimestamp = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var guard = new AttributeNewerThanGuard("attributes.communicationStateTimestamp", newTimestamp);

        var ruleEngineResult = await ruleEngine.ValidateAsync(fixture.TenantId, [
            EntityUpdateInfo<RtEntity>.CreateConditionalUpdate(
                conditionalId,
                new RtEntity("Test/Country", conditionalId.RtId,
                    new Dictionary<string, object?> { { "Designation", "Test" } }),
                guard),
            EntityUpdateInfo<RtEntity>.CreateUpdate(
                unguardedId,
                new RtEntity("Test/Country", unguardedId.RtId,
                    new Dictionary<string, object?> { { "Designation", "Other" } }))
        ], originFileResolver, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.Equal(2, ruleEngineResult.RtEntitiesToUpdate.Count);
        Assert.Single(ruleEngineResult.UpdateGuards);
        Assert.True(ruleEngineResult.UpdateGuards.ContainsKey(conditionalId));
        Assert.False(ruleEngineResult.UpdateGuards.ContainsKey(unguardedId));
        Assert.Same(guard, ruleEngineResult.UpdateGuards[conditionalId]);
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