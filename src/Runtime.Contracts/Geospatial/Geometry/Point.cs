using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

/// <summary>
/// Defines the Point type.
/// In geography, a point refers to a Position on a map, expressed in latitude and longitude.
/// </summary>
/// <remarks>
/// See https://tools.ietf.org/html/rfc7946#section-3.1.2
/// </remarks>
public class Point : GeoJSONObject, IGeometryObject, IEqualityComparer<Point>, IEquatable<Point>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Point" /> class.
    /// </summary>
    /// <param name="coordinates">The Position.</param>
    [System.Text.Json.Serialization.JsonConstructor]
    public Point(IPosition coordinates)
    {
        Coordinates = coordinates;
    }

    /// <inheritdoc cref="IGeometryObject.Type" />
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public override GeoJSONObjectType Type => GeoJSONObjectType.Point;

    /// <summary>
    /// The <see cref="IPosition" /> underlying this point.
    /// </summary>
    [Newtonsoft.Json.JsonProperty("coordinates", Required = Newtonsoft.Json.Required.Always)]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonPositionConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("coordinates")]
    [System.Text.Json.Serialization.JsonConverter(typeof(PositionConverter))]
    public IPosition Coordinates { get; set; }

    #region IEqualityComparer, IEquatable

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return Equals(this, obj as Point);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public bool Equals(Point? other)
    {
        return Equals(this, other);
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public bool Equals(Point? left, Point? right)
    {
        if (ReferenceEquals(left, null))
        {
            return false;
        }
        
        if (ReferenceEquals(null, right))
        {
            return false;
        }
        
        if (base.Equals(left, right))
        {
            return left.Coordinates.Equals(right.Coordinates);
        }
        
        return false;
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public static bool operator ==(Point? left, Point? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (ReferenceEquals(null, right))
        {
            return false;
        }

        return left != null && left.Equals(right);
    }

    /// <summary>
    /// Determines whether the specified object instances are not considered equal
    /// </summary>
    public static bool operator !=(Point? left, Point? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Returns the hash code for this instance
    /// </summary>
    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        hash = (hash * 397) ^ Coordinates.GetHashCode();
        return hash;
    }

    /// <summary>
    /// Returns the hash code for the specified object
    /// </summary>
    public int GetHashCode(Point other)
    {
        return other.GetHashCode();
    }

    #endregion
}