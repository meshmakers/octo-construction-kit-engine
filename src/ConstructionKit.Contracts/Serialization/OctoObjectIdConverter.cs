using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Converter for System.Text.Json and YamlDotNet for <see cref="OctoObjectId"/>
/// </summary>
public class OctoObjectIdConverter : JsonConverter<OctoObjectId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public override OctoObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw new Exception(
                $"Unexpected token parsing ObjectId. Expected String, got {(object)reader.TokenType}.");
        return string.IsNullOrEmpty(str) ? OctoObjectId.Empty : new OctoObjectId(str);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OctoObjectId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value != OctoObjectId.Empty ? value.ToString() : string.Empty);
    }

    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(OctoObjectId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type)
    {
        var value = parser.Consume<Scalar>().Value;
        return new OctoObjectId(value); 
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        var octoObjectId = (OctoObjectId)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, octoObjectId.ToString(), ScalarStyle.Any, true, false));
    }
}
