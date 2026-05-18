using System.Text;
using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class EntityUpdateInfoTests
{
    [Fact]
    public void Serialize_Newtonsoft_RtRecords_OK()
    {
        var rtEntity = new RtEntity("EnergyCommunity/Customer",
            OctoObjectId.Parse("67628789d10f47d162164d34"), new Dictionary<string, object?>([
                new KeyValuePair<string, object?>("FirstName", "Colleen"),
                new KeyValuePair<string, object?>("LastName", "Williamson"),
                new KeyValuePair<string, object?>("CompanyName", "deleniti"),
                new KeyValuePair<string, object?>("Address", new RtRecord("Basic/Address",
                    new Dictionary<string, object?>([
                        new KeyValuePair<string, object?>("Street", "Emmitt Corner"),
                        new KeyValuePair<string, object?>("Zipcode", 1993),
                        new KeyValuePair<string, object?>("CityTown", "South Sheridanchester"),
                        new KeyValuePair<string, object?>("NationalCode", "PA")
                    ])))
            ]));


        var entityUpdateInfo = EntityUpdateInfo<RtEntity>.CreateInsert(rtEntity);

        var stringBuilder = new StringBuilder();
        var jsonWriter = new StringWriter(stringBuilder);
        RtNewtonsoftSerializer.DefaultSerializer.Serialize(jsonWriter, entityUpdateInfo);

        var json = stringBuilder.ToString();


        var expected = @"
            {
              ""RtEntity"" : {
                ""RtId"" : ""67628789d10f47d162164d34"",
                ""CkTypeId"" : ""EnergyCommunity/Customer"",
                ""Attributes"" : {
                  ""FirstName"" : ""Colleen"",
                  ""LastName"" : ""Williamson"",
                  ""CompanyName"" : ""deleniti"",
                  ""Address"" : {
                    ""CkRecordId"" : ""Basic/Address"",
                    ""Attributes"" : {
                      ""Street"" : ""Emmitt Corner"",
                      ""Zipcode"" : 1993,
                      ""CityTown"" : ""South Sheridanchester"",
                      ""NationalCode"" : ""PA""
                    }
                  }
                }
              },
              ""RtId"" : ""67628789d10f47d162164d34"",
              ""CkTypeId"" : ""EnergyCommunity/Customer"",
              ""ModOption"" : 0
            }";
        
        string Normalize(string input)
        {
            // Replace all whitespaces and carriage returns/line feeds with an empty string
            var normalized = Regex.Replace(input, @"\s+", "");
            return normalized.Trim();
        }

        Assert.Equal(Normalize(expected), Normalize(json));
    }

    [Fact]
    public void Deserialize_Newtonsoft_RtRecords_OK()
    {
        var source = @"
            [ {
              ""RtEntity"" : {
                ""RtChangedDateTime"" : ""2024-12-18T06:48:41.814757Z"",
                ""Attributes"" : {
                  ""FirstName"" : ""Colleen"",
                  ""LastName"" : ""Williamson"",
                  ""CompanyName"" : ""deleniti"",
                  ""Address"" : {
                    ""CkRecordId"" : ""Basic/Address"",
                    ""Attributes"" : {
                      ""Street"" : ""Emmitt Corner"",
                      ""Zipcode"" : 1993,
                      ""CityTown"" : ""South Sheridanchester"",
                      ""NationalCode"" : ""PA""
                    }
                  },
                  ""CustomerNumber"" : ""76"",
                  ""AuthenticationEmail"" : ""aut""
                }
              },
              ""CkTypeId"" : ""EnergyCommunity/Customer"",
              ""ModOption"" : 0
            } ]";


        JsonReader reader = new JsonTextReader(new StringReader(source));
        var r = RtNewtonsoftSerializer.DefaultSerializer.Deserialize<List<EntityUpdateInfo<RtEntity>>>(reader);

        Assert.NotNull(r);
        Assert.Single(r);

        var rtEntity = r[0].RtEntity;
        Assert.NotNull(rtEntity);
        Assert.Equal("Colleen", rtEntity.GetAttributeStringValue("FirstName"));
        Assert.Equal("Williamson", rtEntity.GetAttributeStringValue("LastName"));

        var address = rtEntity.GetRtRecordAttributeValueOrDefault<RtRecord>("Address");
        Assert.NotNull(address);
        Assert.Equal("Emmitt Corner", address.GetAttributeStringValue("Street"));
    }

    [Fact]
    public void CreateConditionalUpdate_SetsGuardAndUpdateModOption()
    {
        var rtEntityId = new RtEntityId("EnergyCommunity/Customer", OctoObjectId.GenerateNewId());
        var rtEntity = new RtEntity(rtEntityId.CkTypeId, rtEntityId.RtId,
            new Dictionary<string, object?> { { "FirstName", "Colleen" } });
        var newTimestamp = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        var guard = new AttributeNewerThanGuard("attributes.communicationStateTimestamp", newTimestamp);

        var info = EntityUpdateInfo<RtEntity>.CreateConditionalUpdate(rtEntityId, rtEntity, guard);

        Assert.Equal(EntityModOptions.Update, info.ModOption);
        Assert.Same(rtEntity, info.RtEntity);
        Assert.NotNull(info.UpdateGuard);
        Assert.Equal("attributes.communicationStateTimestamp", info.UpdateGuard.AttributePath);
        Assert.Equal(newTimestamp, info.UpdateGuard.NewValue);
    }

    [Fact]
    public void CreateUpdate_NoGuard_GuardIsNull()
    {
        var rtEntityId = new RtEntityId("EnergyCommunity/Customer", OctoObjectId.GenerateNewId());
        var rtEntity = new RtEntity(rtEntityId.CkTypeId, rtEntityId.RtId,
            new Dictionary<string, object?> { { "FirstName", "Colleen" } });

        var info = EntityUpdateInfo<RtEntity>.CreateUpdate(rtEntityId, rtEntity);

        Assert.Null(info.UpdateGuard);
    }

    [Fact]
    public void Serialize_Newtonsoft_OmitsUpdateGuard()
    {
        // The guard is in-process state only — it must not leak into the cross-process wire
        // format. Round-tripping must not include a "UpdateGuard" field that downstream
        // deserializers would have to know about (and that would surprise the existing
        // RtRecords_OK assertion when extended).
        var rtEntityId = new RtEntityId("EnergyCommunity/Customer", OctoObjectId.GenerateNewId());
        var rtEntity = new RtEntity(rtEntityId.CkTypeId, rtEntityId.RtId,
            new Dictionary<string, object?> { { "FirstName", "Colleen" } });
        var guard = new AttributeNewerThanGuard("attributes.communicationStateTimestamp",
            new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc));
        var info = EntityUpdateInfo<RtEntity>.CreateConditionalUpdate(rtEntityId, rtEntity, guard);

        var stringBuilder = new StringBuilder();
        var jsonWriter = new StringWriter(stringBuilder);
        RtNewtonsoftSerializer.DefaultSerializer.Serialize(jsonWriter, info);
        var json = stringBuilder.ToString();

        Assert.DoesNotContain("UpdateGuard", json);
        Assert.DoesNotContain("AttributePath", json);
    }
}