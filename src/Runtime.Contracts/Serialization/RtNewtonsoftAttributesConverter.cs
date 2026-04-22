using Meshmakers.Octo.ConstructionKit.Contracts;
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
                // JObjects with CkRecordId are deserialized as RtRecord manually
                // (ToObject<RtRecord> fails because of private _attributes field).
                if (jObjectValue.ContainsKey("CkRecordId"))
                {
                    result[prop.Name] = DeserializeRtRecord(jObjectValue, serializer);
                }
                else
                {
                    result[prop.Name] = jObjectValue.ToObject<RtRecord>(serializer);
                }
            }
            else if (prop.Value is JArray jArrayValue)
            {
                // Convert JArray to a List<object?> to avoid serialization issues
                // (e.g. MongoDB BSON serializer does not support JArray).
                // JObjects with CkRecordId are deserialized as RtRecord.
                result[prop.Name] = jArrayValue.Select(item => item switch
                {
                    JValue v => v.Value,
                    JObject obj when obj.ContainsKey("CkRecordId") => (object?)DeserializeRtRecord(obj, serializer),
                    _ => item.ToObject<object?>(serializer)
                }).ToList();
            }
            else if (prop.Value is JValue jValue)
            {
                // Read JValue directly to avoid DefaultValueHandling.Ignore swallowing
                // zero/false/null values during ToObject<object?>() deserialization.
                result[prop.Name] = jValue.Value;
            }
            else
            {
                // Fallback for unexpected token types
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

    /// <summary>
    /// Manually constructs an RtRecord from a JObject that has CkRecordId and Attributes.
    /// Newtonsoft's ToObject fails because RtRecord uses a private _attributes field
    /// that cannot be set via normal deserialization.
    /// </summary>
    private static RtRecord DeserializeRtRecord(JObject obj, JsonSerializer serializer)
    {
        // Parse CkRecordId
        RtCkId<CkRecordId>? ckRecordId = null;
        if (obj.TryGetValue("CkRecordId", out var ckRecordIdToken))
        {
            if (ckRecordIdToken is JObject ckObj)
            {
                var svfn = ckObj["SemanticVersionedFullName"]?.ToString();
                if (svfn != null)
                {
                    ckRecordId = new RtCkId<CkRecordId>(svfn);
                }
            }
            else if (ckRecordIdToken is JValue ckVal)
            {
                var str = ckVal.Value?.ToString();
                if (str != null)
                {
                    ckRecordId = new RtCkId<CkRecordId>(str);
                }
            }
        }

        // Parse Attributes
        var attrs = new Dictionary<string, object?>();
        if (obj.TryGetValue("Attributes", out var attrsToken) && attrsToken is JObject attrsObj)
        {
            foreach (var attrProp in attrsObj.Properties())
            {
                attrs[attrProp.Name] = attrProp.Value switch
                {
                    JValue v => v.Value,
                    JObject innerObj when innerObj.ContainsKey("CkRecordId") =>
                        DeserializeRtRecord(innerObj, serializer),
                    JArray innerArr => innerArr.Select(item => item switch
                    {
                        JValue v => v.Value,
                        JObject arrObj when arrObj.ContainsKey("CkRecordId") =>
                            (object?)DeserializeRtRecord(arrObj, serializer),
                        _ => item.ToObject<object?>(serializer)
                    }).ToList(),
                    _ => attrProp.Value.ToObject<object?>(serializer)
                };
            }
        }

        return ckRecordId != null
            ? new RtRecord(ckRecordId, attrs)
            : new RtRecord { CkRecordId = new RtCkId<CkRecordId>("Unknown") };
    }
}
