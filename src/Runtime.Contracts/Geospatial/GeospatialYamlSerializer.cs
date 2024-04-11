using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial;

/// <summary>
/// Serializes geospatial object types to YAML format.
/// </summary>
public class GeospatialYamlSerializer : IGeospatialYamlSerializer
{
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Creates a new instance of the <see cref="GeospatialYamlSerializer"/> class.
    /// </summary>
    public GeospatialYamlSerializer()
    {
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new PositionConverter())
            .Build();
        
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new PositionConverter())
            .WithTypeConverter(new PointConverter())
            .Build();
    }


    /// <inheritdoc />
    public string Serialize<T>(T obj)
        where T: GeoJSONObject
    {
        return _serializer.Serialize(obj);
    }
    
    /// <inheritdoc />
    public T Deserialize<T>(string yaml)
        where T: GeoJSONObject
    {
        return _deserializer.Deserialize<T>(yaml);
    }
}