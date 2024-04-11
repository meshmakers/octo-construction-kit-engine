using System.Globalization;
using Newtonsoft.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for <see cref="RtEntityId"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtEntityIdConverter : JsonConverter
{
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is RtEntityId rtEntityId)
        {
            writer.WriteValue(rtEntityId.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull();
        }
    }

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonToken.String)
        {
            throw ModelParseException.UnexpectedToken(nameof(RtEntityId), reader.TokenType, nameof(JsonToken.String));
        }

        if (reader.Value == null)
        {
            return null;
        }

        var value = (string)reader.Value;
        return string.IsNullOrEmpty(value) ? default : new RtEntityId(value);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(RtEntityId);
    }
}