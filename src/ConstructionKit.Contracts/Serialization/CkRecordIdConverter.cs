using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkRecordId" />
/// </summary>
public class CkRecordIdConverter : JsonConverter<CkRecordId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkRecordId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkRecordId(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckRecordId = (CkRecordId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckRecordId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkRecordId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkRecordId(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkRecordId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkRecordId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}