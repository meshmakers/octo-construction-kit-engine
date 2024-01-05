using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkAttributeId" />
/// </summary>
public class CkAttributeIdConverter : JsonConverter<CkAttributeId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkAttributeId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkAttributeId(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckAttributeId = (CkAttributeId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckAttributeId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkAttributeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkAttributeId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkAttributeId(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkAttributeId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkAttributeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}