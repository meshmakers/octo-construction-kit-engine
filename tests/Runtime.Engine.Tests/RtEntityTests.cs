using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class RtEntityTests
{
    [Fact]
    public void GetAttributeValues_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new[] { new RtRecord() });

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.NotNull(test);
    }

    [Fact]
    public void GetAttributeValue_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.TimeSpan, "00:05:00");

        var test = rtEntity.GetAttributeValue<TimeSpan>("test");

        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }


    [Fact]
    public void GetAttributeStringValues_StringArray_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.StringArray, new List<object> { "test" });

        var test = rtEntity.GetAttributeStringValues("test");

        Assert.Single(test);
        Assert.Equal("test", test[0]);
    }

    [Fact]
    public void GetRtRecordAttributeValues_RecordArray_Empty_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<object>());

        var test = rtEntity.GetRtRecordAttributeValues<RtRecord>("test");

        Assert.Empty(test);
    }

    [Fact]
    public void GetRtRecordAttributeValues_RecordArray_Untyped_OK()
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
    public void GetRtRecordAttributeValues_RecordArray_Untyped2Typed_OK()
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
    public void GetRtRecordAttributeValues_RecordArray_Typed2Type_OK()
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
    public void GetRtRecordAttributeValues_RecordArray_Typed_Empty_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new List<RtTestRecordRecord>());

        var test = rtEntity.GetRtRecordAttributeValuesOrDefault<RtTestRecordRecord>("test");
        
        Assert.NotNull(test);
        Assert.Empty(test);
    }
}