using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkAttributeId}" />
/// </summary>
public class CkIdAttributeIdConverter : CkIdConverter<CkAttributeId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkTypeId}" />
/// </summary>
public class CkIdTypeIdConverter : CkIdConverter<CkTypeId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkAssociationRoleId}" />
/// </summary>
public class CkIdAssociationRoleIdConverter : CkIdConverter<CkAssociationRoleId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkRecordId}" />
/// </summary>
public class CkIdRecordIdConverter : CkIdConverter<CkRecordId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkEnumId}" />
/// </summary>
public class CkIdEnumIdConverter : CkIdConverter<CkEnumId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{CkInterfaceId}" /> (CK v2, AB#5667)
/// </summary>
public class CkIdInterfaceIdConverter : CkIdConverter<CkInterfaceId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkId{TKey}" />
/// </summary>
/// <typeparam name="TKey"></typeparam>
public class CkIdConverter<TKey> : JsonConverter<CkId<TKey>>, IYamlTypeConverter where TKey : IComparable<TKey>, ICkElementId
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkId<TKey>);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkId<TKey>(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var ckId = (CkId<TKey>)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckId.FullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkId<TKey> ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.PropertyName
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType, nameof(JsonTokenType.PropertyName));

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkId<TKey>(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, CkId<TKey> value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.FullName);
    }

    /// <inheritdoc />
    public override CkId<TKey> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType, nameof(JsonTokenType.String));
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkId<TKey>(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkId<TKey> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.FullName);
    }
}

/// <summary>
///     System.Text.Json converter for a list of <see cref="CkId{CkInterfaceId}" /> (CK v2 <c>implements</c>, AB#5667).
///     A <see cref="JsonConverterAttribute" /> on a list property applies to the list, not to its elements, so the
///     element converter is applied here.
/// </summary>
public class CkIdInterfaceIdListConverter : CkIdListConverter<CkInterfaceId>;

/// <summary>
///     System.Text.Json converter for a list of <see cref="CkId{TKey}" /> that serializes every element as its
///     full-name string (same shape as <see cref="CkIdConverter{TKey}" />).
/// </summary>
/// <typeparam name="TKey"></typeparam>
public class CkIdListConverter<TKey> : JsonConverter<List<CkId<TKey>>> where TKey : IComparable<TKey>, ICkElementId
{
    private readonly CkIdConverter<TKey> _elementConverter = new();

    /// <inheritdoc />
    public override List<CkId<TKey>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw ModelParseException.UnexpectedToken(typeof(TKey).Name, reader.TokenType, nameof(JsonTokenType.StartArray));
        }

        var result = new List<CkId<TKey>>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            result.Add(_elementConverter.Read(ref reader, typeof(CkId<TKey>), options));
        }

        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<CkId<TKey>> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var element in value)
        {
            _elementConverter.Write(writer, element, options);
        }

        writer.WriteEndArray();
    }
}
