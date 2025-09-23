using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="CkModelIdVersionRange" />
/// </summary>
public class CkModelIdVersionRangeConverter : JsonConverter<CkModelIdVersionRange>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(CkModelIdVersionRange);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new CkModelIdVersionRange(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var modelId = (CkModelIdVersionRange)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, modelId.FullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override CkModelIdVersionRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelIdVersionRange), reader.TokenType, nameof(JsonTokenType.String));
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkModelIdVersionRange(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelIdVersionRange));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkModelIdVersionRange value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override CkModelIdVersionRange ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.PropertyName
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkModelIdVersionRange), reader.TokenType, nameof(JsonTokenType.PropertyName));

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkModelIdVersionRange(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkModelIdVersionRange));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, CkModelIdVersionRange value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.FullName);
    }
}