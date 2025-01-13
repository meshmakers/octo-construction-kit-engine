using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
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
    
    [Fact]
    public void GetAttributeValue_Point_Deserialized_OK()
    {
        var point = new Point(new Position(1, 2, 3));
        var rtEntity = new RtEntity("demo/demo", OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            { nameof(RtClient.DPoPClockSkew), point }
        });

        var test = rtEntity.GetAttributeGeometryObjectValue<Point>(nameof(RtClient.DPoPClockSkew));
        Assert.Equal(point, test);
    }
    
    [Fact]
    public void GetAttributeValue_EmbeddedBinary_Deserialized_OK()
    {
        byte[] bytes = [0x5, 0x6, 0x7, 0x8];
        var rtEntity = new RtEntity("demo/demo", OctoObjectId.GenerateNewId(), new Dictionary<string, object?>
        {
            { nameof(RtClient.DPoPClockSkew), bytes }
        });

        var test = rtEntity.GetAttributeBytesValue(nameof(RtClient.DPoPClockSkew));
        Assert.Equal(bytes, test);
    }
}