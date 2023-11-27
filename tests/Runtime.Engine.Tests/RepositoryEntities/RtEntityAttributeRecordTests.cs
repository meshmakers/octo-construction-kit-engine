using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtEntityAttributeRecordTests
{
    [Fact]
    public void GetRtRecordAttributeValues_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new[] { new RtRecord() });

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.NotNull(test);
    }
    
    [Fact]
    public void GetRtRecordAttributeValues_EmptyList_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<object>());

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Empty(test);
    }
    
    [Fact]
    public void GetRtRecordAttributeValues_Null_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.RecordArray, null);

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");
        
        Assert.Empty(test);
    }

    [Fact]
    public void GetRtRecordAttributeValues_Untyped_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtRecord>
        {
            new("Test/TestRecord", new Dictionary<string, object?>
            {
                { "Designation", "TestRecord" },
            })
        });

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Single(test);
        Assert.Equal("TestRecord", test[0].GetAttributeStringValue("Designation"));
    }

    [Fact]
    public void GetRtRecordAttributeValues_Untyped2Typed_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtRecord>
        {
            new("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "TestRecord" },
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
        RtEntity rtEntity = new RtEntity();
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
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtTestRecordRecord>());

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtTestRecordRecord>("test");
        
        Assert.NotNull(test);
        Assert.Empty(test);
    }
    
    [Fact]
    public void GetRtRecordAttributeValuesOrDefault_Null_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.RecordArray, null);

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtTestRecordRecord>("test");
        
        Assert.Null(test);
    }
}