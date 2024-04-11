using System.Reflection;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Features;

/// <summary>
/// A GeoJSON Feature Object; generic version for strongly typed <see cref="Geometry"/>
/// and <see cref="Properties"/>
/// </summary>
/// <remarks>
/// See https://tools.ietf.org/html/rfc7946#section-3.2
/// </remarks>
public class Feature<TGeometry, TProps> : GeoJSONObject, IEquatable<Feature<TGeometry, TProps>>
    where TGeometry : IGeometryObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Feature" /> class.
    /// </summary>
    /// <param name="geometry"></param>
    /// <param name="properties"></param>
    /// <param name="id"></param>
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public Feature(TGeometry geometry, TProps properties, string? id = null)
    {
        Geometry = geometry;
        Properties = properties;
        Id = id;
    }

    /// <inheritdoc />
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public override GeoJSONObjectType Type => GeoJSONObjectType.Feature;

    /// <summary>
    /// Gets the identifier.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "id", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? Id { get; }

    /// <summary>
    /// Gets the Geometry Object.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "geometry", Required = Newtonsoft.Json.Required.AllowNull)]
    [Newtonsoft.Json.JsonConverter(typeof(GeometryConverter))]
    public TGeometry? Geometry { get; }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "properties", Required = Newtonsoft.Json.Required.Default)]
    public TProps Properties { get; }

    /// <summary>
    /// Equality comparer.
    /// </summary>
    /// <remarks>
    /// In contrast to feature equals implementation, this implementation returns true only
    /// if <see cref="Id"/> and <see cref="Properties"/> are also equal. See
    /// <a href="https://github.com/GeoJSON-Net/GeoJSON.Net/issues/80">#80</a> for discussion. The rationale
    /// here is that a user explicitly specifying the property type most probably cares about the properties
    /// equality.
    /// </remarks>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool Equals(Feature<TGeometry, TProps>? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return base.Equals(other)
               && string.Equals(Id, other.Id)
               && EqualityComparer<TGeometry>.Default.Equals(Geometry!, other.Geometry!)
               && EqualityComparer<TProps>.Default.Equals(Properties, other.Properties);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((Feature<TGeometry, TProps>)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = base.GetHashCode();
            hashCode = (hashCode * 397) ^ (Id != null ? Id.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ EqualityComparer<TGeometry>.Default.GetHashCode(Geometry!);
            hashCode = (hashCode * 397) ^ EqualityComparer<TProps>.Default.GetHashCode(Properties!);
            return hashCode;
        }
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator ==(Feature<TGeometry, TProps>? left, Feature<TGeometry, TProps>? right)
    {
        return object.Equals(left, right);
    }

    /// <summary>
    /// Determines whether the specified object instances are not considered equal
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator !=(Feature<TGeometry, TProps>? left, Feature<TGeometry, TProps>? right)
    {
        return !object.Equals(left, right);
    }
}

/// <summary>
/// A GeoJSON Feature Object.
/// </summary>
/// <remarks>
/// See https://tools.ietf.org/html/rfc7946#section-3.2
/// </remarks>
public class Feature : Feature<IGeometryObject>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Feature" /> class.
    /// </summary>
    /// <param name="geometry"></param>
    /// <param name="properties"></param>
    /// <param name="id"></param>
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public Feature(IGeometryObject geometry, IDictionary<string, object?>? properties = null, string? id = null)
        : base(geometry, properties, id)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Feature" /> class.
    /// </summary>
    /// <param name="geometry"></param>
    /// <param name="properties"></param>
    /// <param name="id"></param>
    public Feature(IGeometryObject geometry, object properties, string? id = null)
        : base(geometry, properties, id)
    {
    }
}

/// <summary>
/// Typed GeoJSON Feature class
/// </summary>
/// <remarks>Returns correctly typed Geometry property</remarks>
/// <typeparam name="TGeometry"></typeparam>
public class Feature<TGeometry> : Feature<TGeometry, IDictionary<string, object?>>, IEquatable<Feature<TGeometry>>
    where TGeometry : IGeometryObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Feature" /> class.
    /// </summary>
    /// <param name="geometry">The Geometry Object.</param>
    /// <param name="properties">The properties.</param>
    /// <param name="id">The (optional) identifier.</param>
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public Feature(TGeometry geometry, IDictionary<string, object?>? properties = null, string? id = null)
        : base(geometry, properties ?? new Dictionary<string, object?>(), id)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Feature" /> class.
    /// </summary>
    /// <param name="geometry">The Geometry Object.</param>
    /// <param name="properties">
    /// Class used to fill feature properties. Any public member will be added to feature
    /// properties
    /// </param>
    /// <param name="id">The (optional) identifier.</param>
    public Feature(TGeometry geometry, object properties, string? id = null)
        : this(geometry, GetDictionaryOfPublicProperties(properties), id)
    {
    }

    private static Dictionary<string, object?> GetDictionaryOfPublicProperties(object? properties)
    {
        if (properties == null)
        {
            return new Dictionary<string, object?>();
        }
        var t =  properties.GetType().GetTypeInfo().DeclaredProperties
            .Where(propertyInfo => propertyInfo.GetMethod?.IsPublic ?? false)
            .ToDictionary(propertyInfo => propertyInfo.Name,
                propertyInfo => (object?)propertyInfo.GetValue(properties, null));

        return t;
    }

    /// <inheritdoc />
    public bool Equals(Feature<TGeometry>? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;

        if (Geometry == null && other.Geometry == null)
        {
            return true;
        }

        if (Geometry == null && other.Geometry != null)
        {
            return false;
        }

        if (Geometry == null)
        {
            return false;
        }
        
        return other.Geometry != null && EqualityComparer<TGeometry>.Default.Equals(Geometry, other.Geometry);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((Feature<TGeometry>)obj);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Geometry?.GetHashCode() ?? 0;
    }
    
    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator ==(Feature<TGeometry>? left, Feature<TGeometry>? right)
    {
        return left?.Equals(right) ?? ReferenceEquals(null, right);
    }

    /// <summary>
    /// Determines whether the specified object instances are not considered equal
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator !=(Feature<TGeometry>? left, Feature<TGeometry>? right)
    {
        return !(left?.Equals(right) ?? ReferenceEquals(null, right));
    }
}