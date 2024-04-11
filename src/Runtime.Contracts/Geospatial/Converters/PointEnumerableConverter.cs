using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

/// <summary>
/// Converter to read and write the <see cref="IEnumerable{Point}" /> type.
/// </summary>
public class PointEnumerableConverter : JsonConverter
{
    private static readonly NewtonPositionConverter NewtonPositionConverter = new();
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is IEnumerable<Point> points)
        {
            writer.WriteStartArray();
            foreach (var point in points)
            {
                NewtonPositionConverter.WriteJson(writer, point.Coordinates, serializer);
            }
            writer.WriteEndArray();
        }
        else
        {
            throw new ArgumentException($"{nameof(PointEnumerableConverter)}: unsupported value {value}");
        }
    }

    /// <inheritdoc />
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var coordinates = existingValue as JArray ?? serializer.Deserialize<JArray>(reader);
        var list = new List<Point>();
        if (coordinates != null)
        {
            foreach (var jToken in coordinates)
            {
                var positions = jToken.ToObject<IEnumerable<double>>();
                if (positions != null)
                {
                    list.Add(new Point(positions.ToPosition()));
                }
            }
        }

        return list;
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(IEnumerable<Point>);
    }
}