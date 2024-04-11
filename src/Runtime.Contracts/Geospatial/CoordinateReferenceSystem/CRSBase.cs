using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.CoordinateReferenceSystem;

/// <summary>
/// Base class for all IGeometryObject implementing types
/// </summary>
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
public abstract class CRSBase : IEqualityComparer<CRSBase>, IEquatable<CRSBase>
{
    /// <summary>
    /// Creates a new instance of the CRSBase class
    /// </summary>
    protected CRSBase()
    {
        Properties = new Dictionary<string, object?>();
    }

    /// <summary>
    /// Gets the properties.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "properties", Required = Newtonsoft.Json.Required.Always)]
    [System.Text.Json.Serialization.JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; }

    /// <summary>
    /// Gets the type of the GeometryObject object.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(PropertyName = "type", Required = Newtonsoft.Json.Required.Always)]
    [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonCamelCasingStringEnumConverter))]
    public CRSType Type { get; protected set; }

    #region IEqualityComparer, IEquatable

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public override bool Equals(object? obj)
    {
        return Equals(this, obj as CRSBase);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object
    /// </summary>
    public bool Equals(CRSBase? other)
    {
        return Equals(this, other);
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public bool Equals(CRSBase? left, CRSBase? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (ReferenceEquals(null, right))
        {
            return false;
        }

        if (left.Type != right.Type)
        {
            return false;
        }

        var leftIsNull = ReferenceEquals(null, left.Properties);
        var rightIsNull = ReferenceEquals(null, right.Properties);
        var bothAreMissing = leftIsNull && rightIsNull;

        if (bothAreMissing || leftIsNull != rightIsNull)
        {
            return bothAreMissing;
        }

        if (left.Properties != null)
        {
            foreach (var item in left.Properties)
            {
                if (right.Properties != null)
                {
                    if (!right.Properties.TryGetValue(item.Key, out object? rightValue))
                    {
                        return false;
                    }

                    if (!Equals(item.Value, rightValue))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether the specified object instances are considered equal
    /// </summary>
    public static bool operator ==(CRSBase? left, CRSBase? right)
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
    public static bool operator !=(CRSBase? left, CRSBase? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Returns the hash code for this instance
    /// </summary>
    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        int hashCode = ((int)Type).GetHashCode();
        foreach (var item in Properties)
        {
            string toString;
            if (item.Value == null)
            {
                toString = item.Key;
            }
            else
            {
                toString = $"{item.Key}:{item.Value}";
            }

            hashCode = (hashCode * 397) ^ toString.GetHashCode();
        }

        return hashCode;
    }

    /// <summary>
    /// Returns the hash code for the specified object
    /// </summary>
    public int GetHashCode(CRSBase obj)
    {
        return obj.GetHashCode();
    }

    #endregion
}