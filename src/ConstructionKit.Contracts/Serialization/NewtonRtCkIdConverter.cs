using Newtonsoft.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for <see cref="RtCkId{CkTypeId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkTypeIdConverter : NewtonRtCkIdConverter<CkTypeId>;

/// <summary>
/// Converter for <see cref="RtCkId{CkAttributeId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkAttributeIdConverter : NewtonRtCkIdConverter<CkAttributeId>;

/// <summary>
/// Converter for <see cref="RtCkId{CkEnumId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkEnumIdConverter : NewtonRtCkIdConverter<CkEnumId>;

/// <summary>
/// Converter for <see cref="RtCkId{CkRecordId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkRecordIdConverter : NewtonRtCkIdConverter<CkRecordId>;

/// <summary>
/// Converter for <see cref="RtCkId{CkAssociationRoleId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkAssociationRoleIdConverter : NewtonRtCkIdConverter<CkAssociationRoleId>;

/// <summary>
///     Converter for <see cref="RtCkId{TKey}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonRtCkIdConverter<TKey> : JsonConverter where TKey : IComparable<TKey>, ICkElementId
{
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is RtCkId<TKey> rtCkId)
        {
            writer.WriteValue(rtCkId.ToString());
        }
        else
        {
            writer.WriteNull();
        }
    }

    /// <inheritdoc />
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonToken.String)
        {
            throw ModelParseException.UnexpectedToken(nameof(RtCkId<TKey>), reader.TokenType, nameof(JsonToken.String));
        }

        if (reader.Value == null)
        {
            return null;
        }

        var value = (string)reader.Value;
        return string.IsNullOrEmpty(value) ? null : new RtCkId<TKey>(value);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return typeof(RtCkId<TKey>).IsAssignableFrom(objectType);
    }
}