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