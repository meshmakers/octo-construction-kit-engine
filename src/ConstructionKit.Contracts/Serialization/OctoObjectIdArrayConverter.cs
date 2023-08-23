using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

public class OctoObjectIdArrayConverter : JsonConverter<OctoObjectId[]>
{
    public override OctoObjectId[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new Exception(
                $"Unexpected token parsing ObjectId. Expected start array, got {(object)reader.TokenType}.");
        }

        reader.Read();

        var list = new List<OctoObjectId>();
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            var str = reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : throw new Exception(
                    $"Unexpected token parsing ObjectId. Expected String, got {(object)reader.TokenType}.");
            list.Add(string.IsNullOrEmpty(str) ? OctoObjectId.Empty : new OctoObjectId(str));
            reader.Read();
        }

        return list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, OctoObjectId[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var octoObjectId in value)
        {
            writer.WriteStringValue(octoObjectId != OctoObjectId.Empty ? value.ToString() : string.Empty);
        }

        writer.WriteEndArray();
    }
}
