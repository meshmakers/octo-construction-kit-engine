using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="CkModelId"/>
/// </summary>
public class CkModelIdConverter : JsonConverter<CkModelId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override CkModelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType);
        return !string.IsNullOrEmpty(str) ? new CkModelId(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkModelId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkModelId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkModelId(value); 
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var modelId = (CkModelId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, modelId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}
