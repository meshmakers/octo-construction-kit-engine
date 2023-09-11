using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="CkTypeId"/>
/// </summary>
public class CkTypeIdConverter : JsonConverter<CkTypeId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override CkTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkTypeId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null ? new CkTypeId(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkTypeId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkTypeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkTypeId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkTypeId(value); 
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckTypeId = (CkTypeId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckTypeId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}
