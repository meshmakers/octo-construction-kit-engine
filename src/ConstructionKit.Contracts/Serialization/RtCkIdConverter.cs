using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{CkAttributeId}" />
/// </summary>
public class RtCkIdAttributeIdConverter : RtCkIdConverter<CkAttributeId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{CkTypeId}" />
/// </summary>
public class RtCkIdTypeIdConverter : RtCkIdConverter<CkTypeId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{CkAssociationRoleId}" />
/// </summary>
public class RtCkIdAssociationRoleIdConverter : RtCkIdConverter<CkAssociationRoleId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{CkRecordId}" />
/// </summary>
public class RtCkIdRecordIdConverter : RtCkIdConverter<CkRecordId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{CkEnumId}" />
/// </summary>
public class RtCkIdEnumIdConverter : RtCkIdConverter<CkEnumId>;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="RtCkId{TKey}" />
/// </summary>
/// <typeparam name="TKey"></typeparam>
public class RtCkIdConverter<TKey> : JsonConverter<RtCkId<TKey>>, IYamlTypeConverter where TKey : IComparable<TKey>, ICkElementId
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(RtCkId<TKey>);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new RtCkId<TKey>(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var ckId = (RtCkId<TKey>)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckId.FullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override RtCkId<TKey> ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.PropertyName
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType, nameof(JsonTokenType.PropertyName));

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new RtCkId<TKey>(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, RtCkId<TKey> value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.FullName);
    }

    /// <inheritdoc />
    public override RtCkId<TKey> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType, nameof(JsonTokenType.String));
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new RtCkId<TKey>(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RtCkId<TKey> value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.FullName);
    }
}