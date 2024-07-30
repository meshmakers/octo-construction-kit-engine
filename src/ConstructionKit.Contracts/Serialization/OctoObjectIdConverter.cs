using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="OctoObjectId" />
/// </summary>
public class OctoObjectIdConverter : JsonConverter<OctoObjectId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(OctoObjectId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new OctoObjectId(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var octoObjectId = (OctoObjectId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, octoObjectId.ToString(), ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override OctoObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(OctoObjectId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null ? new OctoObjectId(str) : OctoObjectId.Empty;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OctoObjectId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value != OctoObjectId.Empty ? value.ToString() : string.Empty);
    }
}