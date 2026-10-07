using System.Collections;
using System.Globalization;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
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
///         stays <c>null</c> (clears the secret); the marker is <c>Pending("")</c> ("unchanged"). The marker
///         is an object whose properties are a subset of the read state <c>{ isSet, keyMissing, setAt }</c>
///         (names case-insensitive): <c>isSet</c> and <c>keyMissing</c> booleans, <c>setAt</c> an ISO-8601
///         date string or <c>null</c> - so a client echoing the state object it read
///         (<c>OctoSecretState</c>) leaves the secret unchanged; the empty object is a marker too. Anything
///         else (numbers, booleans, arrays, objects with other properties, a wrongly typed marker property)
///         is rejected with an exception whose message never contains the value (AB#5532 round 2).
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

    /// <summary>
    ///     Name of the optional <c>keyMissing</c> marker property (boolean; accepted on read, never written).
    /// </summary>
    public const string KeyMissingPropertyName = "keyMissing";

    /// <summary>
    ///     Name of the optional <c>setAt</c> marker property (ISO-8601 date string or null; accepted on read,
    ///     never written).
    /// </summary>
    public const string SetAtPropertyName = "setAt";

    private const string ExpectedShape =
        "A Secret value must be a string (the secret to store), null (clear) or the marker {\"isSet\":true|false} " +
        "(unchanged; optional \"keyMissing\": boolean and \"setAt\": date string or null)";

    private enum MarkerProperty
    {
        None,
        Boolean,
        Date
    }

    /// <summary>
    ///     True when the value holds a secret, classified WITHOUT a key ring
    ///     (<see cref="SecretValueStates.GetReadState(RtSecretValue?, Func{string?, bool}?)" /> with <c>null</c>):
    ///     every protected value, a non-empty pending value (a placeholder-looking input is an ordinary
    ///     value), and a legacy value that is neither empty, a legacy placeholder nor corrupt.
    /// </summary>
    /// <remarks>
    ///     The wire marker is written by serializers that have no key ring, so a protected value whose key
    ///     id is not in the ring is marked <c>isSet: true</c> here. APIs that report the read state to users
    ///     (GraphQL <c>isSet</c> / <c>keyMissing</c>, the SDK DTO mapper with a protector) must use
    ///     <see cref="ISecretAttributeProtector.GetReadState" /> instead (decisions 2026-10-06, item 2).
    /// </remarks>
    public static bool IsSet(RtSecretValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SecretValueStates.GetReadState(value, (Func<string?, bool>?)null) == SecretValueState.Set;
    }

    /// <summary>
    ///     True when <paramref name="propertyName" /> is a marker property (case-insensitive):
    ///     <c>isSet</c>, <c>keyMissing</c> or <c>setAt</c>.
    /// </summary>
    public static bool IsMarkerProperty(string? propertyName)
    {
        return Classify(propertyName) != MarkerProperty.None;
    }

    /// <summary>
    ///     True when <paramref name="text" /> is a valid textual value of the marker property
    ///     <paramref name="propertyName" /> (for formats without typed scalars, e.g. YAML): <c>isSet</c> /
    ///     <c>keyMissing</c> need <c>true</c> / <c>false</c>; <c>setAt</c> needs a date or <c>null</c>
    ///     (pass <c>null</c> for a null scalar).
    /// </summary>
    public static bool IsValidMarkerText(string? propertyName, string? text)
    {
        return Classify(propertyName) switch
        {
            MarkerProperty.Boolean => bool.TryParse(text, out _),
            MarkerProperty.Date => text == null || IsDateText(text),
            _ => false
        };
    }

    private static MarkerProperty Classify(string? propertyName)
    {
        if (string.Equals(propertyName, IsSetPropertyName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propertyName, KeyMissingPropertyName, StringComparison.OrdinalIgnoreCase))
        {
            return MarkerProperty.Boolean;
        }

        return string.Equals(propertyName, SetAtPropertyName, StringComparison.OrdinalIgnoreCase)
            ? MarkerProperty.Date
            : MarkerProperty.None;
    }

    private static bool IsDateText(string text)
    {
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);
    }

    private static string InvalidPropertyMessage(MarkerProperty kind, string tokenType)
    {
        return kind == MarkerProperty.Date
            ? $"The property '{SetAtPropertyName}' of a Secret marker must be a date string or null; got a token of type {tokenType}."
            : $"The properties '{IsSetPropertyName}' and '{KeyMissingPropertyName}' of a Secret marker must be booleans; got a token of type {tokenType}.";
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
                    var kind = Classify(reader.GetString());
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (!IsValidMarkerValue(kind, ref reader))
                    {
                        throw new JsonException(InvalidPropertyMessage(kind, reader.TokenType.ToString()));
                    }

                    continue;
                default:
                    throw new JsonException($"{ExpectedShape}; the object has other properties.");
            }
        }

        throw new JsonException("Unexpected end of JSON while reading a Secret marker.");
    }

    private static bool IsValidMarkerValue(MarkerProperty kind, ref Utf8JsonReader reader)
    {
        return kind switch
        {
            MarkerProperty.Boolean => reader.TokenType is JsonTokenType.True or JsonTokenType.False,
            MarkerProperty.Date => reader.TokenType == JsonTokenType.Null ||
                                   (reader.TokenType == JsonTokenType.String && IsDateText(reader.GetString()!)),
            _ => false
        };
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
    ///     True when <paramref name="element" /> is the marker (an object with only the marker properties
    ///     <c>isSet</c> / <c>keyMissing</c> as booleans and <c>setAt</c> as date string or null; may be empty).
    /// </summary>
    public static bool IsMarker(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!IsValidMarkerValue(Classify(property.Name), property.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidMarkerValue(MarkerProperty kind, JsonElement value)
    {
        return kind switch
        {
            MarkerProperty.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            MarkerProperty.Date => value.ValueKind == JsonValueKind.Null ||
                                   (value.ValueKind == JsonValueKind.String && IsDateText(value.GetString()!)),
            _ => false
        };
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
                    var kind = Classify((string?)reader.Value);
                    if (!reader.Read())
                    {
                        break;
                    }

                    if (!IsValidMarkerValue(kind, reader.TokenType, reader.Value))
                    {
                        throw new Newtonsoft.Json.JsonSerializationException(
                            InvalidPropertyMessage(kind, reader.TokenType.ToString()));
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

    private static bool IsValidMarkerValue(MarkerProperty kind, Newtonsoft.Json.JsonToken tokenType, object? value)
    {
        return kind switch
        {
            MarkerProperty.Boolean => tokenType == Newtonsoft.Json.JsonToken.Boolean,
            // Depending on DateParseHandling a date string arrives as a Date token.
            MarkerProperty.Date => tokenType is Newtonsoft.Json.JsonToken.Null or Newtonsoft.Json.JsonToken.Undefined
                                       or Newtonsoft.Json.JsonToken.Date ||
                                   (tokenType == Newtonsoft.Json.JsonToken.String && value is string text &&
                                    IsDateText(text)),
            _ => false
        };
    }

    /// <summary>
    ///     True when <paramref name="token" /> is the marker (an object with only the marker properties
    ///     <c>isSet</c> / <c>keyMissing</c> as booleans and <c>setAt</c> as date (string) or null; may be empty).
    /// </summary>
    public static bool IsMarker(JToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token is JObject obj && obj.Properties().All(p => IsValidMarkerValue(Classify(p.Name), p.Value));
    }

    private static bool IsValidMarkerValue(MarkerProperty kind, JToken value)
    {
        return kind switch
        {
            MarkerProperty.Boolean => value.Type == JTokenType.Boolean,
            MarkerProperty.Date => value.Type is JTokenType.Null or JTokenType.Undefined or JTokenType.Date ||
                                   (value.Type == JTokenType.String && IsDateText((string)value!)),
            _ => false
        };
    }

    #endregion

    #region Object graphs

    /// <summary>
    ///     True when <paramref name="dictionary" /> is the marker: only the keys <c>isSet</c> /
    ///     <c>keyMissing</c> with a boolean value (<see cref="bool" />, a JSON boolean element / token) and
    ///     <c>setAt</c> with a date (<see cref="DateTime" />, <see cref="DateTimeOffset" />, a date string or
    ///     JSON date element / token) or null; may be empty. A non-generic dictionary as YAML produces it
    ///     (<c>Dictionary&lt;object, object&gt;</c> with string scalars) may also hold the text
    ///     <c>true</c> / <c>false</c>.
    /// </summary>
    public static bool IsMarkerDictionary(object dictionary)
    {
        switch (dictionary)
        {
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                return pairs.All(p => IsValidMarkerObject(Classify(p.Key), p.Value, false));
            case IDictionary nonGeneric:
                foreach (DictionaryEntry entry in nonGeneric)
                {
                    if (entry.Key is not string key || !IsValidMarkerObject(Classify(key), entry.Value, true))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static bool IsValidMarkerObject(MarkerProperty kind, object? value, bool allowText)
    {
        return kind switch
        {
            MarkerProperty.Boolean => value switch
            {
                bool => true,
                JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => true,
                JValue { Type: JTokenType.Boolean } => true,
                string text when allowText => bool.TryParse(text, out _),
                _ => false
            },
            MarkerProperty.Date => value switch
            {
                null => true,
                DateTime or DateTimeOffset => true,
                string text => IsDateText(text),
                JsonElement element => IsValidMarkerValue(kind, element),
                JToken token => IsValidMarkerValue(kind, token),
                _ => false
            },
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
