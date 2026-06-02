using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for serializing and deserializing <see cref="OctoObjectId" /> arrays
/// </summary>
public class OctoObjectIdArrayConverter : JsonConverter<OctoObjectId[]>
{
    /// <inheritdoc />
    public override OctoObjectId[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw ModelParseException.UnexpectedToken(nameof(OctoObjectId), reader.TokenType, nameof(JsonTokenType.StartArray));
        }

        reader.Read();

        var list = new List<OctoObjectId>();
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            var str = reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : throw ModelParseException.UnexpectedToken(nameof(OctoObjectId), reader.TokenType, nameof(JsonTokenType.String));
            list.Add(!string.IsNullOrEmpty(str) && str != null ? new OctoObjectId(str) : OctoObjectId.Empty);
            reader.Read();
        }

        return list.ToArray();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OctoObjectId[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var octoObjectId in value)
        {
            writer.WriteStringValue(octoObjectId != OctoObjectId.Empty ? octoObjectId.ToString() : string.Empty);
        }

        writer.WriteEndArray();
    }
}