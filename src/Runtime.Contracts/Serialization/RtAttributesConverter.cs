using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// System.Text.Json counterpart of <see cref="RtNewtonsoftAttributesConverter"/>. Materializes the
/// attribute dictionary of <see cref="RtTypeWithAttributes"/> (the base of <see cref="RtEntity"/> and
/// <see cref="RtRecord"/>) on deserialize.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <see cref="RtTypeWithAttributes.Attributes"/> is a get-only
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> over a private backing field. STJ serializes the
/// getter fine, but on deserialize it cannot write a get-only property and would silently discard the
/// whole dictionary — the classic Newtonsoft→STJ trap. <see cref="RtEntity"/> / <see cref="RtRecord"/>
/// therefore carry <c>[JsonConstructor]</c> on the attribute-taking constructor so STJ routes the
/// dictionary through the constructor, and this converter (registered against the constructor's
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> parameter type) does the value materialization.
/// </para>
/// <para>
/// <b>Value materialization</b> mirrors the production round-trip behaviour of
/// <see cref="RtNewtonsoftAttributesConverter"/> (in-memory <c>JObject.FromObject</c> →
/// <c>JToken.ToObject</c>, which preserves the source CLR type in <c>JValue.Value</c>):
/// integers that fit in <see cref="int"/> stay <see cref="int"/>, larger integers become
/// <see cref="long"/>; reals stay <see cref="double"/>; ISO-8601 strings become
/// <see cref="DateTime"/>; JSON objects carrying a <c>CkRecordId</c> become nested
/// <see cref="RtRecord"/> instances; arrays become <see cref="List{T}"/> of <see cref="object"/>.
/// Producing CLR scalars (rather than leaving raw <see cref="JsonElement"/>s) is required by
/// downstream consumers such as <c>GetAttributeValue&lt;T&gt;</c> (which calls
/// <see cref="Convert.ChangeType(object, Type)"/>) and by the MongoDB BSON serializer, which
/// dispatches on the value's CLR type.
/// </para>
/// <para>
/// The boxing rules are enforced as a contract by
/// <c>Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests</c>, which uses
/// Newtonsoft as the oracle. Some divergences are irreducible (float vs double, decimal vs double,
/// DateTimeOffset vs DateTime — JSON has no source-CLR-type marker for those); they are listed
/// in <c>AttributeValueParityCorpus.IrreducibleDivergences</c>.
/// </para>
/// </remarks>
public sealed class RtAttributesConverter : JsonConverter<IReadOnlyDictionary<string, object?>>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Dictionary<string, object?>();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                $"Expected '{JsonTokenType.StartObject}' for an attribute dictionary but found '{reader.TokenType}'.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return MaterializeObject(document.RootElement, options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var keyValuePair in value)
        {
            writer.WritePropertyName(keyValuePair.Key);
            // Serialize by runtime type so nested RtRecords (and CLR scalars) round-trip with the
            // same shape Newtonsoft produced. Matches RtNewtonsoftAttributesConverter.WriteJson.
            JsonSerializer.Serialize(writer, keyValuePair.Value, keyValuePair.Value?.GetType() ?? typeof(object),
                options);
        }

        writer.WriteEndObject();
    }

    private static Dictionary<string, object?> MaterializeObject(JsonElement element, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = MaterializeValue(property.Value, options);
        }

        return result;
    }

    private static object? MaterializeValue(JsonElement element, JsonSerializerOptions options)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // Objects carrying a CkRecordId are runtime records; everything else is a plain
                // nested attribute map. RtRecord deserialization recurses back into this converter
                // via the record's [JsonConstructor] attributes parameter.
                return element.TryGetProperty("CkRecordId", out _)
                    ? element.Deserialize<RtRecord>(options)
                    : MaterializeObject(element, options);

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(MaterializeValue(item, options));
                }

                return list;

            default:
                // Scalar arms (String, Number, True, False, Null, Undefined) are handled by the
                // single-source boxing primitive so the rules stay in one place.
                return JsonScalar.ToClr(element, parseDateStrings: true);
        }
    }
}
