using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkEnumId" />
/// </summary>
public class CkEnumIdConverter : JsonConverter<CkEnumId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkEnumId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkEnumId(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var ckEnumId = (CkEnumId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckEnumId.SemanticVersionedFullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkEnumId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkEnumId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkEnumId(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkEnumId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkEnumId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}