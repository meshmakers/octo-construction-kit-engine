using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

public class CkModelIdConverter : JsonConverter<CkModelId>, IYamlTypeConverter
{
    public override CkModelId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelId), reader.TokenType);
        return !string.IsNullOrEmpty(str) ? new CkModelId(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelId));
    }

    public override void Write(Utf8JsonWriter writer, CkModelId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    public bool Accepts(Type type)
    {
        return type == typeof(CkModelId);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkModelId(value); 
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var modelId = (CkModelId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, modelId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}
