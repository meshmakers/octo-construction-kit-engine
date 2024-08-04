
using Newtonsoft.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for serializing and deserializing <see cref="OctoObjectId" /> arrays
/// </summary>
public class NewtonOctoObjectIdEnumerableConverter : JsonConverter
{
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is IEnumerable<OctoObjectId> octoObjectIds)
        {
            writer.WriteStartArray();
            foreach (var octoObjectId in octoObjectIds)
            {
                writer.WriteValue(octoObjectId != OctoObjectId.Empty ? octoObjectId.ToString() : string.Empty);
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull();
        }
    }

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonToken.StartArray)
        {
            throw ModelParseException.UnexpectedToken(nameof(OctoObjectId), reader.TokenType,
                nameof(JsonToken.StartArray));
        }


        var list = new List<OctoObjectId>();
        while (reader.TokenType != JsonToken.EndArray)
        {
            var str = reader.ReadAsString();
            if (reader.TokenType != JsonToken.EndArray)
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                list.Add(!string.IsNullOrEmpty(str) && str != null ? new OctoObjectId(str) : OctoObjectId.Empty);
            }
        }

        return list.ToArray();
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return typeof(IEnumerable<OctoObjectId>).IsAssignableFrom(objectType);
    }
}