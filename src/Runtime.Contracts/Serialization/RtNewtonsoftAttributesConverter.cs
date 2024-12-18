using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// Deserializes a dictionary of attributes, where the value can be a RtRecord.
/// </summary>
public class RtNewtonsoftAttributesConverter : JsonConverter<Dictionary<string, object?>>
{
    /// <inheritdoc />
    public override Dictionary<string, object?> ReadJson(JsonReader reader, Type objectType,
        Dictionary<string, object?>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);
        var result = existingValue ?? new Dictionary<string, object?>();

        foreach (var prop in jObject.Properties())
        {
            if (prop.Value is JObject jObjectValue)
            {
                // Deserializes a record object
                var rtRecord =jObjectValue.ToObject<RtRecord>(serializer);
                result[prop.Name] = rtRecord;
            }
            else
            {
                // Default deserialization
                result[prop.Name] = prop.Value.ToObject<object?>(serializer);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, Dictionary<string, object?>? value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        foreach (var keyValuePair in value!)
        {
            writer.WritePropertyName(keyValuePair.Key);
            serializer.Serialize(writer, keyValuePair.Value);
        }
        writer.WriteEndObject();
    }
}