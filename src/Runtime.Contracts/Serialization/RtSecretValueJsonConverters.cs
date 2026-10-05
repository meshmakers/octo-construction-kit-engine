using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
///     Wire shape of an <see cref="RtSecretValue" /> in every engine JSON serialisation (AB#5532): only the
///     marker <c>{"isSet":true|false}</c> is written - never the envelope, never a plaintext. Reading
///     accepts a string (new input, <see cref="RtSecretValue.Pending" />), the marker or any object
///     (<c>Pending("")</c> = "unchanged" for the write path) and <c>null</c>. Same contract as the octo-sdk
///     converters (octo-sdk e9a570d).
/// </summary>
public static class RtSecretValueWireFormat
{
    /// <summary>
    ///     Name of the marker property.
    /// </summary>
    public const string IsSetPropertyName = "isSet";

    /// <summary>
    ///     True when the value holds a secret: a protected value, or a pending / legacy value that is
    ///     neither empty nor a placeholder.
    /// </summary>
    public static bool IsSet(RtSecretValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.IsProtected ||
               (value.RawValue.Length > 0 && !SecretAttributeConventions.IsPlaceholder(value.RawValue));
    }
}

/// <summary>
///     System.Text.Json converter of <see cref="RtSecretValue" /> (marker only, see
///     <see cref="RtSecretValueWireFormat" />). Applied to the type, so every options instance uses it.
/// </summary>
public sealed class RtSecretValueJsonConverter : System.Text.Json.Serialization.JsonConverter<RtSecretValue>
{
    /// <inheritdoc />
    public override RtSecretValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return RtSecretValue.Pending(reader.GetString() ?? string.Empty);
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                // The marker (or anything structured) carries no value: "unchanged".
                reader.Skip();
                return RtSecretValue.Pending(string.Empty);
            default:
                throw new JsonException($"Unexpected token '{reader.TokenType}' for a Secret value.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RtSecretValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean(RtSecretValueWireFormat.IsSetPropertyName, RtSecretValueWireFormat.IsSet(value));
        writer.WriteEndObject();
    }
}

/// <summary>
///     Newtonsoft converter of <see cref="RtSecretValue" /> (marker only, see
///     <see cref="RtSecretValueWireFormat" />). Applied to the type, so every serializer uses it.
/// </summary>
public sealed class RtSecretValueNewtonsoftConverter : Newtonsoft.Json.JsonConverter<RtSecretValue>
{
    /// <inheritdoc />
    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, RtSecretValue? value,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(RtSecretValueWireFormat.IsSetPropertyName);
        writer.WriteValue(RtSecretValueWireFormat.IsSet(value));
        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public override RtSecretValue? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType,
        RtSecretValue? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        var token = JToken.Load(reader);
        return token.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => null,
            JTokenType.String => RtSecretValue.Pending(token.Value<string>() ?? string.Empty),
            JTokenType.Object or JTokenType.Array => RtSecretValue.Pending(string.Empty),
            _ => RtSecretValue.Pending(token.ToString(Newtonsoft.Json.Formatting.None))
        };
    }
}
