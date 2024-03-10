using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using TestCkModel.Generated.System.TestIdentity.v1;

namespace Meshmakers.Octo.Runtime.Engine.Tests.RepositoryEntities;

public class RtEntityTests
{
    [Fact]
    public void GetAttributeValue_TimeSpan_OK()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("test", AttributeValueTypesDto.TimeSpan, "00:05:00");

        var test = rtEntity.GetAttributeValue<TimeSpan>("test");

        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }

    [Fact]
    public void GetAttributeValue_TimeSpan_Deserialized_OK()
    {
        var rtEntity = new RtEntity("demo/demo", OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            { nameof(RtClient.DPoPClockSkew), "00:05:00" }
        });

        var test = rtEntity.GetAttributeValue<TimeSpan>(nameof(RtClient.DPoPClockSkew));
        Assert.Equal(TimeSpan.FromMinutes(5), test);
    }
}