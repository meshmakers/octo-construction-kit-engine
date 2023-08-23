using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

public class CkTypeIdConverter : JsonConverter<CkTypeId>, IYamlTypeConverter
{
    public override CkTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkTypeId), reader.TokenType);
        return !string.IsNullOrEmpty(str) ? new CkTypeId(str) : throw ModelParseException.ValueCannotBeEmpty(nameof(CkTypeId));
    }

    public override void Write(Utf8JsonWriter writer, CkTypeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    public bool Accepts(Type type)
    {
        return type == typeof(CkTypeId);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkTypeId(value); 
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var ckTypeId = (CkTypeId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckTypeId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }
}
