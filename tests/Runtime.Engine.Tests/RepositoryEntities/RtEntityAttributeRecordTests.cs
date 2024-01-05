using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;
using TestCkModel.ConstructionKit.Generated.System.TestIdentity.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtEntityAttributeRecordTests
{
    [Fact]
    public void GetRtRecordAttributeValues_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new[] { new RtRecord() });

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.NotNull(test);
    }

    [Fact]
    public void GetRtRecordAttributeValues_EmptyList_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<object>());

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Empty(test);
    }

    [Fact]
    public void GetRtRecordAttributeValues_Null_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.RecordArray, null);

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Empty(test);
    }

    [Fact]
    public void GetRtRecordAttributeValues_Untyped_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtRecord>
        {
            new("Test/TestRecord", new Dictionary<string, object?>
            {
                { "Designation", "TestRecord" }
            })
        });

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Single(test);
        Assert.Equal("TestRecord", test[0].GetAttributeStringValue("Designation"));
    }

    [Fact]
    public void GetRtRecordAttributeValues_Untyped2Typed_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtRecord>
        {
            new("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "TestRecord" }
                }
            )
        });

        var test = rtEntity.GetRtRecordAttributeValues<RtTestRecordRecord>("test");

        Assert.Single(test);
        Assert.Equal("TestRecord", test[0].GetAttributeStringValue("Designation"));
    }

    [Fact]
    public void GetRtRecordAttributeValues_Typed2Typed_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtTestRecordRecord>
        {
            new() { Designation = "TestRecord", CkRecordId = "Test/TestRecord" }
        });

        var test = rtEntity.GetRtRecordAttributeValues<RtTestRecordRecord>("test");

        Assert.Single(test);
        Assert.Equal("TestRecord", test[0].GetAttributeStringValue("Designation"));
    }

    [Fact]
    public void GetRtRecordAttributeValuesOrDefault_Typed_EmptyList_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtTestRecordRecord>());

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtTestRecordRecord>("test");

        Assert.NotNull(test);
        Assert.Empty(test);
    }

    [Fact]
    public void GetRtRecordAttributeValuesOrDefault_Null_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.RecordArray, null);

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtTestRecordRecord>("test");

        Assert.Null(test);
    }

    [Fact]
    public void GetRtRecordAttributeValuesOrDefault_Deserialized_OK()
    {
        var rtEntity = new RtEntity("demo/demo", OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            { nameof(RtRole.Claims), new List<object>() }
        });

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtRoleClaimRecord>(nameof(RtRole.Claims));

        Assert.NotNull(test);
        Assert.Empty(test);
    }
}