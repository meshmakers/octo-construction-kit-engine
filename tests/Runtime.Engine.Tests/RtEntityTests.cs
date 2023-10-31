using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class RtEntityTests
{

    [Fact]
    public void GetAttributeValues_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.RecordArray, new [] { new RtRecord() });
        
        var test = rtEntity.GetAttributeValues<RtRecord>("test");
        
        Assert.NotNull(test);
    }
    
    [Fact]
    public void GetAttributeValue_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.TimeSpan,"00:05:00");
        
        var test = rtEntity.GetAttributeValue<TimeSpan>("test");
        
        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }
    
    
        [Fact]
        public void GetAttributeValues_StringArray_OK()
        {
            RtEntity rtEntity = new RtEntity();
            rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.StringArray,new List<object> {"test"});
            
            var test = rtEntity.GetAttributeValues<string>("test");
            
            Assert.Single(test);
            Assert.Equal("test", test[0]);
        }
}