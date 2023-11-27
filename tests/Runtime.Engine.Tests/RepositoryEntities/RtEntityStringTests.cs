using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using TestCkModel.ConstructionKit.Generated.System.TestIdentity.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtEntityStringTests
{
    [Fact]
    public void GetAttributeStringValues_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("test", AttributeValueTypesDto.StringArray, new[] { "demo" });

        var test = rtEntity.GetAttributeStringValues("test");

        Assert.Single(test);
        Assert.Equal("demo", test[0]);
    }
    
    [Fact]
    public void GetAttributeStringValues_Null2EmptyForNonNullable_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.StringArray, null);

        var test = rtEntity.GetAttributeStringValues("test");

        Assert.Empty(test);
    }
    
    [Fact]
    public void GetAttributeStringValues_Null2NullForNullable_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.StringArray, null);

        var test = rtEntity.GetAttributeStringValuesOrDefault("test");

        Assert.Null(test);
    }
    
    [Fact]
    public void GetAttributeStringValues_Null2Value_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.StringArray, null);
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.StringArray, new[] { "demo" });

        var test = rtEntity.GetAttributeStringValuesOrDefault("test");

        Assert.NotNull(test);
        Assert.Single(test);
        Assert.Equal("demo", test[0]);
    }
    
    [Fact]
    public void GetAttributeStringValues_Generated_OK()
    {
        RtClient rtEntity = new RtClient
        {
            AllowedCorsOrigins = { "https://test.com" }
        };

        var test = rtEntity.AllowedCorsOrigins;

        Assert.Single(test);
        Assert.Equal("https://test.com", test[0]);
    }
    
    [Fact]
    public void GetAttributeStringValues_Generated_Empty_OK()
    {
        RtClient rtEntity = new RtClient
        {
            AllowedCorsOrigins = {  }
        };

        var test = rtEntity.AllowedCorsOrigins;

        Assert.Empty(test);
    }
}