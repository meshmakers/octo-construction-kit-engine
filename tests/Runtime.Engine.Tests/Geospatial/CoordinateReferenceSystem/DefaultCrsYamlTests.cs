using Meshmakers.Octo.Runtime.Contracts.Geospatial;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Features;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Geospatial.CoordinateReferenceSystem;

public class DefaultCrsYamlTests
{
    private static readonly string Nl = Environment.NewLine;
    
    [Fact]
    public void Can_Serialize_Does_Not_Output_Crs_Property()
    {
        var collection = new FeatureCollection();

        var serializer = new GeospatialYamlSerializer();

        var yaml = serializer.Serialize(collection);

        Assert.DoesNotContain("\"crs\"", yaml);
    }

    [Fact]
    public void Can_Deserialize_When_Yaml_Does_Not_Contain_Crs_Property()
    {
        var yaml = $"coordinates:{Nl}- 90.65464646{Nl}- 53.2455662{Nl}- 200.4567{Nl}type: Point";

        var serializer = new GeospatialYamlSerializer();
        var point = serializer.Deserialize<Point>(yaml);

        Assert.NotNull(point);
        Assert.Null(point.CRS); 
    }

    [Fact]
    public void Can_Deserialize_CRS_issue_89()
    {
        var yaml = $"coordinates:{Nl}- 90.65464646{Nl}- 53.2455662{Nl}- 200.4567{Nl}type: Point{Nl}crs:{Nl}  type: name{Nl}  properties:{Nl}    name: urn:ogc:def:crs:OGC:1.3:CRS84";


        var serializer = new GeospatialYamlSerializer();
        var point = serializer.Deserialize<Point>(yaml);

        Assert.NotNull(point);
        Assert.NotNull(point.CRS);
        Assert.Equal(CRSType.Name, point.CRS.Type);
    }

    [Fact]
    public void Can_Serialize_CRS_issue_89()
    {
        var expected = $"type: Point{Nl}coordinates:{Nl}- 34.56{Nl}- 12.34{Nl}crs:{Nl}  properties:{Nl}    name: TEST NAME{Nl}  type: name{Nl}";
        var point = new Point(new Position(12.34, 34.56)) { CRS = new NamedCRS("TEST NAME") };

        var serializer = new GeospatialYamlSerializer();
        var yaml = serializer.Serialize(point);

        Assert.NotNull(yaml);
        Assert.Equal(expected, yaml);
    }
    
}