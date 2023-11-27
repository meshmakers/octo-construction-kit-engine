using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Engine.Tests.sampleData.CkTest.ConstructionKit.Generated.Test.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtEntityTests
{


    [Fact]
    public void GetAttributeValue_OK()
    {
        RtEntity rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.TimeSpan, "00:05:00");

        var test = rtEntity.GetAttributeValue<TimeSpan>("test");

        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }


}