using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Features;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Geospatial.CoordinateReferenceSystem;

public class DefaultCrsSystemTextJsonTests
{
    [Fact]
    public void Can_Serialize_Does_Not_Output_Crs_Property()
    {
        var collection = new FeatureCollection();

        var json = JsonSerializer.Serialize(collection);

        Assert.DoesNotContain("\"crs\"", json);
    }

    [Fact]
    public void Can_Deserialize_When_Json_Does_Not_Contain_Crs_Property()
    {
        var json = "{\"coordinates\":[90.65464646,53.2455662,200.4567],\"type\":\"Point\"}";

        var point = JsonSerializer.Deserialize<Point>(json);

        Assert.NotNull(point);
        Assert.Null(point.CRS); 
    }

    [Fact]
    public void Can_Deserialize_CRS_issue_89()
    {
        var json =
            "{\"coordinates\": [ 90.65464646, 53.2455662, 200.4567 ], \"type\": \"Point\", \"crs\": { \"type\": \"name\", \"properties\": { \"name\": \"urn:ogc:def:crs:OGC:1.3:CRS84\" }}}";

        var point = JsonSerializer.Deserialize<Point>(json);

        Assert.NotNull(point);
        Assert.NotNull(point.CRS);
        Assert.Equal(CRSType.Name, point.CRS.Type);
    }

}