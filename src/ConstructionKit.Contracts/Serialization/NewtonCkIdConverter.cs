using Newtonsoft.Json;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for <see cref="CkId{CkTypeId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkTypeIdConverter : NewtonCkIdConverter<CkTypeId>;

/// <summary>
/// Converter for <see cref="CkId{CkAttributeId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkAttributeIdConverter : NewtonCkIdConverter<CkAttributeId>;

/// <summary>
/// Converter for <see cref="CkId{CkEnumId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkEnumIdConverter : NewtonCkIdConverter<CkEnumId>;

/// <summary>
/// Converter for <see cref="CkId{CkRecordId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkRecordIdConverter : NewtonCkIdConverter<CkRecordId>;

/// <summary>
/// Converter for <see cref="CkId{CkAssociationRoleId}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkAssociationRoleIdConverter : NewtonCkIdConverter<CkAssociationRoleId>;

/// <summary>
///     Converter for <see cref="CkId{TKey}"/> to JSON using Newtonsoft.Json
/// </summary>
public class NewtonCkIdConverter<TKey> : JsonConverter where TKey : IComparable<TKey>, ICkKey
{
    /// <inheritdoc />
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is CkId<TKey> ckId)
        {
            writer.WriteValue(ckId.ToString());
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
        if (reader.TokenType != JsonToken.String)
        {
            throw new Exception($"Unexpected token parsing CkId. Expected String, got {reader.TokenType}.");
        }

        if (reader.Value == null)
        {
            return null;
        }

        var value = (string)reader.Value;
        return string.IsNullOrEmpty(value) ? null : new CkId<TKey>(value);
    }

    /// <inheritdoc />
    public override bool CanConvert(Type objectType)
    {
        return typeof(CkId<TKey>).IsAssignableFrom(objectType);
    }
}