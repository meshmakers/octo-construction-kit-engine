using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RuleEngine;

/// <summary>
///     AB#7445: validation of atomic RecordArray item updates. Test/Country has the optional-free RecordArray
///     <c>RecordArrayTests</c> of <c>Test/TestRecord</c> (Designation: String, DataCount: Int).
/// </summary>
public class RecordArrayItemUpdateRuleEngineTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    private static readonly OctoObjectId EntityId = OctoObjectId.Parse("67628789d10f47d162164d34");

    private static string ToJson(object value)
    {
        var writer = new StringWriter();
        RtNewtonsoftSerializer.DefaultSerializer.Serialize(writer, value);
        return writer.ToString();
    }

    private static EntityUpdateInfo<RtEntity> FromJson(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json));
        return RtNewtonsoftSerializer.DefaultSerializer.Deserialize<EntityUpdateInfo<RtEntity>>(reader)!;
    }

    private static EntityUpdateInfo<RtEntity> Info(RtRecordArrayItemUpdate update,
        Dictionary<string, object?>? regularAttributes = null)
    {
        var id = new RtEntityId("Test/Country", EntityId);
        var entity = new RtEntity("Test/Country", EntityId, regularAttributes ?? new Dictionary<string, object?>());
        return EntityUpdateInfo<RtEntity>.CreateRecordArrayItemUpdate(id, entity, [update]);
    }

    private static RtRecordArrayItemUpdate Update(string attribute = "RecordArrayTests", string match = "Designation",
        object? matchValue = null, Dictionary<string, object?>? members = null) =>
        new(attribute, match, matchValue ?? "A", members ?? new Dictionary<string, object?> { ["DataCount"] = 5 });

    private async Task<(Contracts.RuleEngine.EntityRuleEngineResult<RtEntity> Result, OperationResult Operation)>
        ValidateAsync(params IEntityUpdateInfo<RtEntity>[] infos)
    {
        var ruleEngine = new EntityRuleEngine(await fixture.GetCacheServiceAsync());
        var operationResult = new OperationResult();
        var result = await ruleEngine.ValidateAsync(fixture.TenantId, infos, new OriginFileResolver("Test"),
            operationResult);
        return (result, operationResult);
    }

    [Fact]
    public async Task ValidItemUpdate_IsPassedOnWithConvertedValues()
    {
        var (result, operation) = await ValidateAsync(Info(Update(members: new Dictionary<string, object?>
        {
            ["DataCount"] = "7"
        })));

        Assert.Empty(operation.Messages);
        var entry = Assert.Single(result.RecordArrayItemUpdates);
        Assert.Contains(entry.Key, result.RtEntitiesToUpdate.Keys);
        var item = Assert.Single(entry.Value);
        Assert.Equal("RecordArrayTests", item.AttributeName);
        Assert.Equal("Designation", item.MatchMember);
        Assert.Equal("A", item.MatchValue);
        Assert.Equal(7, Assert.IsType<int>(item.MemberUpdates["DataCount"]));
    }

    [Fact]
    public async Task EntriesForTheSameItem_AreMerged_LastWins()
    {
        var id = new RtEntityId("Test/Country", EntityId);
        var info = EntityUpdateInfo<RtEntity>.CreateRecordArrayItemUpdate(id,
            new RtEntity("Test/Country", EntityId, new Dictionary<string, object?>()),
            [
                Update(members: new Dictionary<string, object?> { ["DataCount"] = 1 }),
                Update(members: new Dictionary<string, object?> { ["DataCount"] = 2 })
            ]);

        var (result, operation) = await ValidateAsync(info);

        Assert.Empty(operation.Messages);
        var item = Assert.Single(Assert.Single(result.RecordArrayItemUpdates).Value);
        Assert.Equal(2, item.MemberUpdates["DataCount"]);
    }

    [Fact]
    public async Task AttributeThatIsNoRecordArray_IsRefused_NamingAttribute()
    {
        var (result, operation) = await ValidateAsync(Info(Update(attribute: "Designation")));

        Assert.True(operation.HasFatalErrors);
        Assert.Empty(result.RecordArrayItemUpdates);
        Assert.Empty(result.RtEntitiesToUpdate);
        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("Test/Country", message.ToString());
        Assert.Contains("Designation", message.ToString());
    }

    [Fact]
    public async Task UnknownMember_IsRefused_NamingMember()
    {
        var (_, operation) = await ValidateAsync(Info(Update(members: new Dictionary<string, object?>
        {
            ["Bogus"] = 1
        })));

        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("RecordArrayTests", message.ToString());
        Assert.Contains("Bogus", message.ToString());
    }

    [Fact]
    public async Task UnknownMatchMember_IsRefused()
    {
        var (_, operation) = await ValidateAsync(Info(Update(match: "Nope")));

        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("Nope", message.ToString());
    }

    [Fact]
    public async Task WrongValueType_IsRefused()
    {
        var (result, operation) = await ValidateAsync(Info(Update(members: new Dictionary<string, object?>
        {
            ["DataCount"] = "not-a-number"
        })));

        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("DataCount", message.ToString());
        Assert.Empty(result.RtEntitiesToUpdate);
    }

    [Fact]
    public async Task NumberForStringMember_IsRefused_NotStringified()
    {
        var (_, operation) = await ValidateAsync(Info(Update(matchValue: "A", members: new Dictionary<string, object?>
        {
            ["Designation"] = 12
        })));

        // Designation is the match member here: refused because it cannot be written by the item update
        Assert.Equal(25, Assert.Single(operation.Messages).MessageNumber);
    }

    [Fact]
    public async Task WritingTheMatchMember_IsRefused()
    {
        var (_, operation) = await ValidateAsync(Info(Update(members: new Dictionary<string, object?>
        {
            ["Designation"] = "B"
        })));

        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("match member", message.ToString());
    }

    [Fact]
    public async Task WholeValueAndItemUpdateOfTheSameArray_IsRefusedAsAmbiguous()
    {
        var (_, operation) = await ValidateAsync(Info(Update(),
            new Dictionary<string, object?> { ["RecordArrayTests"] = new List<RtRecord>() }));

        var message = Assert.Single(operation.Messages);
        Assert.Equal(25, message.MessageNumber);
        Assert.Contains("ambiguous", message.ToString());
    }

    [Fact]
    public async Task ItemUpdateWithRegularAttributes_AreValidatedTogether()
    {
        var (result, operation) = await ValidateAsync(Info(Update(),
            new Dictionary<string, object?> { ["IsEnabled"] = true }));

        Assert.Empty(operation.Messages);
        Assert.Single(result.RecordArrayItemUpdates);
        Assert.Single(result.RtEntitiesToUpdate);
    }

    [Fact]
    public async Task ItemUpdateOnInsert_IsRefused()
    {
        var update = Update();
        var entity = new RtEntity("Test/Country", EntityId, new Dictionary<string, object?>());
        var (_, operation) = await ValidateAsync(new InsertWithItemUpdates(entity, [update]));

        Assert.Contains(operation.Messages, m => m.MessageNumber == 25 && m.MessageLevel == MessageLevel.FatalError);
    }

    private sealed class InsertWithItemUpdates(RtEntity entity, IReadOnlyCollection<RtRecordArrayItemUpdate> updates)
        : IEntityUpdateInfo<RtEntity>
    {
        public RtEntity? RtEntity => entity;
        public OctoObjectId? RtId => entity.RtId;
        public RtCkId<CkTypeId> CkTypeId => "Test/Country";
        public EntityModOptions ModOption => EntityModOptions.Insert;
        public AttributeNewerThanGuard? UpdateGuard => null;
        public IReadOnlyCollection<RtRecordArrayItemUpdate>? RecordArrayItemUpdates => updates;
        public RtEntityId GetRtEntityId() => new(CkTypeId, entity.RtId);
    }

    [Fact]
    public void InfoWithoutItemUpdates_KeepsTheWireShape()
    {
        var info = EntityUpdateInfo<RtEntity>.CreateUpdate(new RtEntityId("Test/Country", EntityId),
            new RtEntity("Test/Country", EntityId, new Dictionary<string, object?>()));

        Assert.Null(info.RecordArrayItemUpdates);
        Assert.DoesNotContain("RecordArrayItemUpdates", ToJson(info));
    }

    [Fact]
    public void ItemUpdates_SurviveANewtonsoftRoundTrip()
    {
        var info = Info(Update(members: new Dictionary<string, object?> { ["DataCount"] = 3 }));

        var clone = FromJson(ToJson(info));

        var item = Assert.Single(clone.RecordArrayItemUpdates!);
        Assert.Equal("RecordArrayTests", item.AttributeName);
        Assert.Equal("Designation", item.MatchMember);
        Assert.Equal("A", item.MatchValue);
        Assert.Equal(3L, Convert.ToInt64(item.MemberUpdates["DataCount"]));
    }

    [Fact]
    public void CreateRecordArrayItemUpdate_WithoutUpdates_Throws()
    {
        Assert.Throws<ArgumentException>(() => EntityUpdateInfo<RtEntity>.CreateRecordArrayItemUpdate(
            new RtEntityId("Test/Country", EntityId), new RtEntity(), []));
    }
}
