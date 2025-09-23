using System.Collections.ObjectModel;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

 /// <summary>
    /// Contains an array of <see cref="Point" />.
    /// </summary>
    /// <remarks>
    /// See https://tools.ietf.org/html/rfc7946#section-3.1.3
    /// </remarks>
    public class MultiPoint : GeoJSONObject, IGeometryObject, IEqualityComparer<MultiPoint>, IEquatable<MultiPoint>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiPoint" /> class.
        /// </summary>
        /// <param name="coordinates">The coordinates.</param>
        public MultiPoint(IEnumerable<Point>? coordinates)
        {
            Coordinates = new ReadOnlyCollection<Point>(coordinates?.ToArray() ?? []);
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiPoint" /> class.
        /// </summary>
        /// <param name="coordinates"></param>
        [Newtonsoft.Json.JsonConstructor]
        [System.Text.Json.Serialization.JsonConstructor]
        public MultiPoint(IEnumerable<IEnumerable<double>>? coordinates)
        : this(coordinates?.Select(position => new Point(position.ToPosition()))
               ?? throw new ArgumentNullException(nameof(coordinates)))
        {
        }

        /// <inheritdoc cref="IGeometryObject.Type" />
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public override GeoJSONObjectType Type => GeoJSONObjectType.MultiPoint;

        /// <summary>
        /// The points contained in this <see cref="MultiPoint"/>.
        /// </summary>
        [Newtonsoft.Json.JsonProperty("coordinates", Required = Newtonsoft.Json.Required.Always)]
        [Newtonsoft.Json.JsonConverter(typeof(PointEnumerableConverter))]
        [System.Text.Json.Serialization.JsonPropertyName("coordinates")]
        [System.Text.Json.Serialization.JsonConverter(typeof(PositionConverter))]
        public ReadOnlyCollection<Point> Coordinates { get; }

        #region IEqualityComparer, IEquatable

        /// <summary>
        /// Determines whether the specified object is equal to the current object
        /// </summary>
        public override bool Equals(object? obj)
        {
            return Equals(this, obj as MultiPoint);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object
        /// </summary>
        public bool Equals(MultiPoint? other)
        {
            return Equals(this, other);
        }

        /// <summary>
        /// Determines whether the specified object instances are considered equal
        /// </summary>
        public bool Equals(MultiPoint? left, MultiPoint? right)
        {
            if (base.Equals(left, right))
            {
                if (ReferenceEquals(left, null))
                {
                    return false;
                }
        
                if (ReferenceEquals(null, right))
                {
                    return false;
                }
                
                return left.Coordinates.SequenceEqual(right.Coordinates);
            }
            return false;
        }

        /// <summary>
        /// Determines whether the specified object instances are considered equal
        /// </summary>
        public static bool operator ==(MultiPoint? left, MultiPoint? right)
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
        public static bool operator !=(MultiPoint? left, MultiPoint? right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns the hash code for this instance
        /// </summary>
        public override int GetHashCode()
        {
            int hash = base.GetHashCode();
            foreach (var item in Coordinates)
            {
                hash = (hash * 397) ^ item.GetHashCode();
            }
            return hash;
        }

        /// <summary>
        /// Returns the hash code for the specified object
        /// </summary>
        public int GetHashCode(MultiPoint other)
        {
            return other.GetHashCode();
        }

        #endregion
    }