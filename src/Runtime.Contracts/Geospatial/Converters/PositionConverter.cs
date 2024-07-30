using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Geospatial.Converters;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="IPosition" />
/// </summary>
public class PositionConverter : JsonConverter<IPosition>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override IPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var coordinates = new List<double> { 0, 0 };
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return coordinates.ToPosition();
                }

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    var propertyName = reader.GetString();
                    reader.Read();

                    if (propertyName == "longitude")
                    {
                        coordinates[0] = reader.GetDouble();
                    }
                    else if (propertyName == "latitude")
                    {
                        coordinates[1] = reader.GetDouble();
                    }
                    else if (propertyName == "altitude")
                    {
                        coordinates.Add(reader.GetDouble());
                    }
                    else
                    {
                        throw RuntimeModelParseException.UnexpectedToken(nameof(IPosition), reader.TokenType,
                            nameof(JsonTokenType.PropertyName));
                    }
                }
                else
                {
                    throw RuntimeModelParseException.UnexpectedToken(nameof(IPosition), reader.TokenType,
                        nameof(JsonTokenType.PropertyName));
                }
            }
        }
        else
        {
            List<double> coordinates = new List<double>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return coordinates.ToPosition();
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    coordinates.Add(reader.GetDouble());
                }
                else
                {
                    throw RuntimeModelParseException.UnexpectedToken(nameof(IPosition), reader.TokenType,
                        nameof(JsonTokenType.Number));
                }
            }
        }

        throw RuntimeModelParseException.UnexpectedEndOfStream(nameof(IPosition));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IPosition value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        writer.WriteNumberValue(value.Longitude);
        writer.WriteNumberValue(value.Latitude);

        if (value.Altitude.HasValue)
        {
            writer.WriteNumberValue(value.Altitude.Value);
        }

        writer.WriteEndArray();
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return typeof(IPosition).IsAssignableFrom(type);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var coordinates = new List<double>();

        try
        {
            parser.Consume<SequenceStart>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                var scalar = parser.Consume<Scalar>();
                coordinates.Add(double.Parse(scalar.Value, CultureInfo.InvariantCulture));
            }
            return coordinates.ToPosition();
        }
        catch (Exception e)
        {
            throw RuntimeModelParseException.UnexpectedFormat(nameof(IPosition), e);
        }
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        if (value is IPosition position)
        {
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
            emitter.Emit(new Scalar(null, null, position.Longitude.ToString(CultureInfo.InvariantCulture), ScalarStyle.Plain, true, false));
            emitter.Emit(new Scalar(null, null, position.Latitude.ToString(CultureInfo.InvariantCulture), ScalarStyle.Plain, true, false));
            if (position.Altitude.HasValue)
            {
                emitter.Emit(new Scalar(null, null, position.Altitude.Value.ToString(CultureInfo.InvariantCulture), ScalarStyle.Plain, true, false));
            }
            emitter.Emit(new SequenceEnd());
        }
        else
        {
            throw RuntimeModelParseException.InvalidType(typeof(IPosition), value);
        }
    }
}