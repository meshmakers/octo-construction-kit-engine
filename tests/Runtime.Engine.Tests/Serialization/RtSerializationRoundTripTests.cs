using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

/// <summary>
/// Full serialize → deserialize round-trips through <see cref="RtSystemTextJsonSerializer.Default"/>
/// for the <c>RtTypeWithAttributes</c> family. Complements
/// <see cref="RtAttributesConverterCharacterizationTests"/> (which only deserializes hand-written
/// JSON) by proving the get-only <c>Attributes</c> dictionary survives a real round-trip — the
/// Newtonsoft→STJ trap that <see cref="RtAttributesConverter"/> + <c>[JsonConstructor]</c> exist to
/// prevent — across <see cref="RtEntity"/> (including nested <see cref="RtRecord"/> materialization),
/// <see cref="RtAssociation"/>, and <see cref="StreamDataEntity"/>.
///
/// These describe observed behavior and MUST keep passing after every serialization refactor.
/// </summary>
public class RtSerializationRoundTripTests
{
    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, RtSystemTextJsonSerializer.Default);
        var result = JsonSerializer.Deserialize<T>(json, RtSystemTextJsonSerializer.Default);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void RtEntity_ScalarAttributes_SurviveRoundTrip()
    {
        var measuredAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var entity = new RtEntity(new RtCkId<CkTypeId>("Test/T"), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>
            {
                ["count"] = 42L,
                ["ratio"] = 3.14,
                ["name"] = "hello world",
                ["enabled"] = true,
                ["measuredAt"] = measuredAt
            });

        var result = RoundTrip(entity);

        // The get-only Attributes dictionary must NOT be silently dropped on deserialize.
        Assert.Equal(5, result.Attributes.Count);
        Assert.Equal(42L, result.GetAttributeValue<long>("count"));
        Assert.Equal(3.14, result.GetAttributeValue<double>("ratio"));
        Assert.Equal("hello world", (string?)result.Attributes["name"]);
        Assert.True(result.GetAttributeValue<bool>("enabled"));
        // DateTime equality is tick-based; the UTC instant must survive the ISO round-trip.
        Assert.Equal(measuredAt, result.GetAttributeValue<DateTime>("measuredAt"));
    }

    [Fact]
    public void RtEntity_NestedRtRecordAttribute_MaterializesAsRtRecord()
    {
        var inner = new RtRecord(new RtCkId<CkRecordId>("Test/TimeRange"),
            new Dictionary<string, object?>
            {
                ["from"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ["label"] = "q1"
            });
        var entity = new RtEntity(new RtCkId<CkTypeId>("Test/T"), OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["range"] = inner });

        var result = RoundTrip(entity);

        // A nested object carrying a CkRecordId must materialize back as an RtRecord (not a plain
        // dictionary) — the discriminator arm of RtAttributesConverter.MaterializeValue.
        Assert.True(result.Attributes.TryGetValue("range", out var rangeValue));
        var record = Assert.IsType<RtRecord>(rangeValue);
        Assert.Equal(inner.CkRecordId.ToString(), record.CkRecordId.ToString());
        Assert.Equal("q1", (string?)record.Attributes["label"]);
    }

    [Fact]
    public void RtEntity_NestedPlainObject_MaterializesAsDictionary_NotRtRecord()
    {
        // A nested object WITHOUT a CkRecordId discriminator is a plain attribute map, not a runtime
        // record. Pins the deliberate behavior: only CkRecordId-bearing objects become RtRecord;
        // everything else stays a Dictionary<string, object?> (the STJ converter does not guess at
        // RtRecord the way the legacy Newtonsoft converter's ToObject<RtRecord> attempted to).
        const string json = """{"CkTypeId":"Test/T","RtId":"","Attributes":{"meta":{"k":"v","n":5}}}""";
        var entity = JsonSerializer.Deserialize<RtEntity>(json, RtSystemTextJsonSerializer.Default);

        Assert.NotNull(entity);
        Assert.True(entity!.Attributes.TryGetValue("meta", out var meta));
        var map = Assert.IsType<Dictionary<string, object?>>(meta);
        Assert.Equal("v", map["k"]);
        // Small ints box to Int32 (Newtonsoft parity — see JsonScalar.ToClr).
        Assert.Equal(5, map["n"]);
    }

    [Fact]
    public void RtAssociation_AttributesSurviveRoundTrip()
    {
        var assoc = new RtAssociation(new RtCkId<CkAssociationRoleId>("Test/Role"),
            OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?> { ["weight"] = 7L, ["note"] = "primary" })
        {
            OriginRtId = OctoObjectId.GenerateNewId(),
            TargetRtId = OctoObjectId.GenerateNewId(),
            OriginCkTypeId = new RtCkId<CkTypeId>("Test/A"),
            TargetCkTypeId = new RtCkId<CkTypeId>("Test/B")
        };

        var result = RoundTrip(assoc);

        Assert.Equal(2, result.Attributes.Count);
        Assert.Equal(7L, result.GetAttributeValue<long>("weight"));
        Assert.Equal("primary", (string?)result.Attributes["note"]);
    }

    [Fact]
    public void StreamDataEntity_AttributesSurviveRoundTrip()
    {
        var timestamp = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var entity = new StreamDataEntity(new Dictionary<string, object?>
        {
            ["temperature"] = 21.5,
            ["unit"] = "C"
        })
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>("Test/Sensor"),
            Timestamp = timestamp
        };

        var result = RoundTrip(entity);

        Assert.Equal(2, result.Attributes.Count);
        Assert.Equal(21.5, result.GetAttributeValue<double>("temperature"));
        Assert.Equal("C", (string?)result.Attributes["unit"]);
        Assert.Equal(timestamp, result.Timestamp);
    }
}
