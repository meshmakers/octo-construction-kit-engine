namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

/// <summary>
/// Base Interface for GeometryObject types.
/// </summary>
public interface IGeometryObject
{
    /// <summary>
    /// Gets the (mandatory) type of the GeoJSON Object.
    /// However, for GeoJSON Objects only the 'Point', 'MultiPoint', 'LineString', 'MultiLineString', 
    /// 'Polygon', 'MultiPolygon', or 'GeometryCollection' types are allowed.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc7946#section-3.1
    /// </remarks>
    /// <value>
    /// The type of the object.
    /// </value>
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    GeoJSONObjectType Type { get; }
}