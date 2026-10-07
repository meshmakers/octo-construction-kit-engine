using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkInterfaceId" />
/// </summary>
public class CkInterfaceIdConverter : JsonConverter<CkInterfaceId>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkInterfaceId);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkInterfaceId(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var ckInterfaceId = (CkInterfaceId)value!;
        // Unlike type ids, an interface id always carries its version: it IS the contract version
        // (the schema requires the '-<version>' suffix), so the full name is written even for version 1.
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, ckInterfaceId.FullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkInterfaceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkInterfaceId), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkInterfaceId(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkInterfaceId));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkInterfaceId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}