using Meshmakers.Octo.ConstructionKit.Contracts;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Converter for <see cref="OctoObjectId" /> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonOctoObjectIdConverter : JsonConverter
{
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is OctoObjectId objectId)
        {
            writer.WriteValue(objectId != OctoObjectId.Empty ? objectId.ToString() : string.Empty);
        }
        else
        {
            throw new Exception("Expected ObjectId value.");
        }
    }

    /// <inheritdoc />
    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.String)
        {
            throw new Exception($"Unexpected token parsing ObjectId. Expected String, got {reader.TokenType}.");
        }

        if (reader.Value == null)
        {
            return OctoObjectId.Empty;
        }

        var value = (string)reader.Value;
        return string.IsNullOrEmpty(value) ? OctoObjectId.Empty : new OctoObjectId(value);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return typeof(OctoObjectId).IsAssignableFrom(objectType);
    }
}