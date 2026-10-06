using System.Collections;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     The one wire contract of an <see cref="RtSecretValue" /> in every serialisation (AB#5532, AB#5534).
/// </summary>
/// <remarks>
///     <para>
///         <b>Write:</b> only the marker <c>{"isSet":true|false}</c> - never the envelope, never a plaintext.
///     </para>
///     <para>
///         <b>Read:</b> a string is new input (<see cref="RtSecretValue.Pending" />, not trimmed); <c>null</c>
///         stays <c>null</c> (clears the secret); the marker - an empty object or an object whose only
///         property is <c>isSet</c> (case-insensitive) with a boolean value - is <c>Pending("")</c>
///         ("unchanged"). Anything else (numbers, booleans, arrays, objects with other properties, a
///         non-boolean <c>isSet</c>) is rejected with an exception whose message never contains the value.
///     </para>
///     <para>
///         The System.Text.Json, Newtonsoft and YAML converters of the engine and the octo-sdk converters
///         all implement exactly this contract through the helpers of this class.
///     </para>
/// </remarks>
public static class RtSecretValueWireFormat
{
    /// <summary>
    ///     Name of the marker property.
    /// </summary>
    public const string IsSetPropertyName = "isSet";

    private const string ExpectedShape =
        "A Secret value must be a string (the secret to store), null (clear) or the marker {\"isSet\":true|false} (unchanged)";

    /// <summary>
    ///     True when the value holds a secret: a protected value, a non-empty pending value (a
    ///     placeholder-looking input is an ordinary value), or a legacy value that is neither empty nor a
    ///     legacy placeholder (migration only).
    /// </summary>
    public static bool IsSet(RtSecretValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.State switch
        {
            RtSecretValueState.Protected => true,
            RtSecretValueState.Pending => value.RawValue.Length > 0,
            _ => value.RawValue.Length > 0 && !SecretAttributeConventions.IsLegacyPlaceholder(value.RawValue)
        };
    }

    /// <summary>
    ///     True when <paramref name="propertyName" /> is the marker property (case-insensitive).
    /// </summary>
    public static bool IsMarkerProperty(string? propertyName)
    {
        return string.Equals(propertyName, IsSetPropertyName, StringComparison.OrdinalIgnoreCase);
    }

    #region System.Text.Json

    /// <summary>
    ///     Reads an <see cref="RtSecretValue" /> strictly (see remarks of <see cref="RtSecretValueWireFormat" />).
    ///     The reader is positioned on the first token of the value and is left on its last token.
    /// </summary>
    /// <exception cref="JsonException">Any other shape; the message never contains the value.</exception>
    public static RtSecretValue? Read(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return RtSecretValue.Pending(reader.GetString() ?? string.Empty);
            case JsonTokenType.StartObject:
                ReadMarker(ref reader);
                return RtSecretValue.Pending(string.Empty);
            default:
                throw new JsonException($"{ExpectedShape}; got a token of type {reader.TokenType}.");
        }
    }

    private static void ReadMarker(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndObject:
                    return;
                case JsonTokenType.Comment:
                    continue;
                case JsonTokenType.PropertyName when IsMarkerProperty(reader.GetString()):
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
                    {
                        throw new JsonException(
                            $"The property '{IsSetPropertyName}' of a Secret marker must be a boolean; got a token of type {reader.TokenType}.");
                    }

                    continue;
                default:
                    throw new JsonException($"{ExpectedShape}; the object has other properties.");
            }
        }

        throw new JsonException("Unexpected end of JSON while reading a Secret marker.");
    }

    /// <summary>
    ///     Writes the marker <c>{"isSet":true|false}</c> of <paramref name="value" />.
    /// </summary>
    public static void Write(Utf8JsonWriter writer, RtSecretValue value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteBoolean(IsSetPropertyName, IsSet(value));
        writer.WriteEndObject();
    }

    /// <summary>
    ///     True when <paramref name="element" /> is the marker (empty object or only a boolean <c>isSet</c>).
    /// </summary>
    public static bool IsMarker(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!IsMarkerProperty(property.Name) ||
                (property.Value.ValueKind != JsonValueKind.True && property.Value.ValueKind != JsonValueKind.False))
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region Newtonsoft

    /// <summary>
    ///     Reads an <see cref="RtSecretValue" /> strictly with Newtonsoft (see remarks of
    ///     <see cref="RtSecretValueWireFormat" />). The reader is positioned on the first token of the
    ///     value and is left on its last token.
    /// </summary>
    /// <exception cref="Newtonsoft.Json.JsonSerializationException">
    ///     Any other shape; the message never contains the value.
    /// </exception>
    public static RtSecretValue? Read(Newtonsoft.Json.JsonReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (reader.TokenType is Newtonsoft.Json.JsonToken.None or Newtonsoft.Json.JsonToken.Comment)
        {
            if (!reader.Read())
            {
                throw new Newtonsoft.Json.JsonSerializationException("Unexpected end of JSON while reading a Secret value.");
            }
        }

        switch (reader.TokenType)
        {
            case Newtonsoft.Json.JsonToken.Null:
            case Newtonsoft.Json.JsonToken.Undefined:
                return null;
            case Newtonsoft.Json.JsonToken.String:
                return RtSecretValue.Pending((string?)reader.Value ?? string.Empty);
            case Newtonsoft.Json.JsonToken.StartObject:
                ReadMarker(reader);
                return RtSecretValue.Pending(string.Empty);
            default:
                throw new Newtonsoft.Json.JsonSerializationException(
                    $"{ExpectedShape}; got a token of type {reader.TokenType}.");
        }
    }

    private static void ReadMarker(Newtonsoft.Json.JsonReader reader)
    {
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case Newtonsoft.Json.JsonToken.EndObject:
                    return;
                case Newtonsoft.Json.JsonToken.Comment:
                    continue;
                case Newtonsoft.Json.JsonToken.PropertyName when IsMarkerProperty((string?)reader.Value):
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (reader.TokenType != Newtonsoft.Json.JsonToken.Boolean)
                    {
                        throw new Newtonsoft.Json.JsonSerializationException(
                            $"The property '{IsSetPropertyName}' of a Secret marker must be a boolean; got a token of type {reader.TokenType}.");
                    }

                    continue;
                default:
                    throw new Newtonsoft.Json.JsonSerializationException($"{ExpectedShape}; the object has other properties.");
            }
        }

        throw new Newtonsoft.Json.JsonSerializationException("Unexpected end of JSON while reading a Secret marker.");
    }

    /// <summary>
    ///     Writes the marker <c>{"isSet":true|false}</c> of <paramref name="value" />, or <c>null</c>.
    /// </summary>
    public static void Write(Newtonsoft.Json.JsonWriter writer, RtSecretValue? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(IsSetPropertyName);
        writer.WriteValue(IsSet(value));
        writer.WriteEndObject();
    }

    /// <summary>
    ///     True when <paramref name="token" /> is the marker (empty object or only a boolean <c>isSet</c>).
    /// </summary>
    public static bool IsMarker(JToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token is JObject obj &&
               obj.Properties().All(p => IsMarkerProperty(p.Name) && p.Value.Type == JTokenType.Boolean);
    }

    #endregion

    #region Object graphs

    /// <summary>
    ///     True when <paramref name="dictionary" /> is the marker: empty, or only the key <c>isSet</c> with a
    ///     boolean value (<see cref="bool" />, a JSON boolean element / token). A non-generic dictionary as
    ///     YAML produces it (<c>Dictionary&lt;object, object&gt;</c> with string scalars) may also hold the
    ///     text <c>true</c> / <c>false</c>.
    /// </summary>
    public static bool IsMarkerDictionary(object dictionary)
    {
        switch (dictionary)
        {
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                return pairs.All(p => IsMarkerProperty(p.Key) && IsBoolean(p.Value, false));
            case IDictionary nonGeneric:
                foreach (DictionaryEntry entry in nonGeneric)
                {
                    if (entry.Key is not string key || !IsMarkerProperty(key) || !IsBoolean(entry.Value, true))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static bool IsBoolean(object? value, bool allowText)
    {
        return value switch
        {
            bool => true,
            JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => true,
            JValue { Type: JTokenType.Boolean } => true,
            string text when allowText => bool.TryParse(text, out _),
            _ => false
        };
    }

    #endregion
}

/// <summary>
///     System.Text.Json converter of <see cref="RtSecretValue" /> (strict marker contract, see
///     <see cref="RtSecretValueWireFormat" />). Applied to the type, so every options instance uses it.
/// </summary>
public sealed class RtSecretValueJsonConverter : System.Text.Json.Serialization.JsonConverter<RtSecretValue>
{
    /// <inheritdoc />
    public override RtSecretValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return RtSecretValueWireFormat.Read(ref reader);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RtSecretValue value, JsonSerializerOptions options)
    {
        RtSecretValueWireFormat.Write(writer, value);
    }
}

/// <summary>
///     Newtonsoft converter of <see cref="RtSecretValue" /> (strict marker contract, see
///     <see cref="RtSecretValueWireFormat" />). Applied to the type, so every serializer uses it.
/// </summary>
public sealed class RtSecretValueNewtonsoftConverter : Newtonsoft.Json.JsonConverter<RtSecretValue>
{
    /// <inheritdoc />
    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, RtSecretValue? value,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        RtSecretValueWireFormat.Write(writer, value);
    }

    /// <inheritdoc />
    public override RtSecretValue? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType,
        RtSecretValue? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        return RtSecretValueWireFormat.Read(reader);
    }
}
