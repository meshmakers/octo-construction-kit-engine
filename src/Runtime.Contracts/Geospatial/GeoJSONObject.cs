using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial;

/// <summary>
///     Base class for all IGeometryObject implementing types
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
public abstract class GeoJSONObject : IGeoJSONObject, IEqualityComparer<GeoJSONObject>, IEquatable<GeoJSONObject>
{
    internal static readonly DoubleTenDecimalPlaceComparer DoubleComparer = new();

    /// <summary>
    ///     Gets or sets the (optional)
    ///     <a href="http://tools.ietf.org/html/rfc7946#section-5">Bounding Boxes</a>.
    /// </summary>
    /// <value>
    ///     The value of <see cref="BoundingBoxes" /> must be a 2*n array where n is the number of dimensions represented in
    ///     the
    ///     contained geometries, with the lowest values for all axes followed by the highest values.
    ///     The axes order of a bbox follows the axes order of geometries.
    ///     In addition, the coordinate reference system for the bbox is assumed to match the coordinate reference
    ///     system of the GeoJSON object of which it is a member.
    /// </value>
    [Newtonsoft.Json.JsonProperty(PropertyName = "bbox", Required = Newtonsoft.Json.Required.Default,
        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonPropertyName("bbox")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    [YamlMember(Alias = "bbox", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public double[]? BoundingBoxes { get; set; }

    /// <summary>
    ///     Gets or sets the (optional)
    ///     <a href="http://tools.ietf.org/html/rfc7946#section-4">
    ///         Coordinate Reference System
    ///         Object.
    ///     </a>
    /// </summary>
    /// <value>
    ///     The Coordinate Reference System Objects.
    /// </value>
    [Newtonsoft.Json.JsonProperty(PropertyName = "crs", Required = Newtonsoft.Json.Required.Default,
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.IgnoreAndPopulate,
        NullValueHandling = Newtonsoft.Json.NullValueHandling.Include)]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonCrsConverter))]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]    
    [System.Text.Json.Serialization.JsonPropertyName("crs")]
    [System.Text.Json.Serialization.JsonConverter(typeof(CrsConverter))]
    [YamlMember(Alias = "crs")]
    public ICRSObject? CRS { get; set; }

    /// <summary>
    ///     The (mandatory) type of the
    ///     <a href="http://tools.ietf.org/html/rfc7946#section-3">GeoJSON Object</a>.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "type", Required = Newtonsoft.Json.Required.Always,
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include)]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public abstract GeoJSONObjectType Type { get; }


    #region IEqualityComparer, IEquatable

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return Equals(this, obj as GeoJSONObject);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public bool Equals(GeoJSONObject? other)
    {
        return Equals(this, other);
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public bool Equals(GeoJSONObject? left, GeoJSONObject? right)
    {
        if (ReferenceEquals(left, null))
        {
            return false;
        }
        
        if (ReferenceEquals(null, right))
        {
            return false;
        }
        
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Type != right.Type)
        {
            return false;
        }

        if (!Equals(left.CRS, right.CRS))
        {
            return false;
        }

        var leftIsNull = ReferenceEquals(null, left.BoundingBoxes);
        var rightIsNull = ReferenceEquals(null, right.BoundingBoxes);
        var bothAreMissing = leftIsNull && rightIsNull;

        if (bothAreMissing || leftIsNull != rightIsNull)
        {
            return bothAreMissing;
        }
        
        return left.BoundingBoxes?.SequenceEqual(right.BoundingBoxes!, DoubleComparer) ?? false;
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public static bool operator ==(GeoJSONObject left, GeoJSONObject right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (ReferenceEquals(null, right))
        {
            return false;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether the specified object instances are not considered equal
    /// </summary>
    public static bool operator !=(GeoJSONObject left, GeoJSONObject right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Returns the hash code for this instance
    /// </summary>
    public override int GetHashCode()
    {
        return ((int)Type).GetHashCode();
    }

    /// <summary>
    /// Returns the hash code for the specified object
    /// </summary>
    public int GetHashCode(GeoJSONObject obj)
    {
        return obj.GetHashCode();
    }

    #endregion
}