using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

/// <summary>
/// Converter to read and write the <see cref="IEnumerable{LineString}" /> type.
/// </summary>
public class LineStringEnumerableConverter : JsonConverter
{
    private static readonly PositionEnumerableConverter LineStringConverter = new();

    /// <summary>
    /// Determines whether this instance can convert the specified object type.
    /// </summary>
    /// <param name="objectType">Type of the object.</param>
    /// <returns>
    /// <c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
    /// </returns>
    public override bool CanConvert(Type objectType)
    {
        return typeof(IEnumerable<LineString>).IsAssignableFrom(objectType);
    }

    /// <summary>
    /// Reads the JSON representation of the object.
    /// </summary>
    /// <param name="reader">The <see cref="T:Newtonsoft.Json.JsonReader" /> to read from.</param>
    /// <param name="objectType">Type of the object.</param>
    /// <param name="existingValue">The existing value of object being read.</param>
    /// <param name="serializer">The calling serializer.</param>
    /// <returns>
    /// The object value.
    /// </returns>
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var rings = existingValue as JArray ?? serializer.Deserialize<JArray>(reader);
        var r = rings?.Select(ring => new LineString((IEnumerable<IPosition>)LineStringConverter.ReadJson(
                reader,
                typeof(IEnumerable<IPosition>),
                ring,
                serializer)))
            .ToArray();
        
        return r ?? [];
    }

    /// <summary>
    /// Writes the JSON representation of the object.
    /// </summary>
    /// <param name="writer">The <see cref="T:Newtonsoft.Json.JsonWriter" /> to write to.</param>
    /// <param name="value">The value.</param>
    /// <param name="serializer">The calling serializer.</param>
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is IEnumerable<LineString> coordinateElements)
        {
            writer.WriteStartArray();
            foreach (var subPolygon in coordinateElements)
            {
                LineStringConverter.WriteJson(writer, subPolygon.Coordinates, serializer);
            }

            writer.WriteEndArray();
        }
        else
        {
            throw new ArgumentException($"{nameof(LineStringEnumerableConverter)}: unsupported value type");
        }
    }
}