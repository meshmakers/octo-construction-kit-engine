using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

/// <summary>
///     Converter for System.Text.Json and YamlDotNet for <see cref="BlueprintIdVersionRange" />
/// </summary>
public class BlueprintIdVersionRangeConverter : JsonConverter<BlueprintIdVersionRange>, IYamlTypeConverter
{
    /// <inheritdoc />
    public bool Accepts(Type type)
    {
        return type == typeof(BlueprintIdVersionRange);
    }

    /// <inheritdoc />
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var value = parser.Consume<Scalar>().Value;
        return new BlueprintIdVersionRange(value);
    }

    /// <inheritdoc />
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        var blueprintId = (BlueprintIdVersionRange)value!;
        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, blueprintId.FullName, ScalarStyle.Any, true, false));
    }

    /// <inheritdoc />
    public override BlueprintIdVersionRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(BlueprintIdVersionRange), reader.TokenType, nameof(JsonTokenType.String));
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new BlueprintIdVersionRange(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(BlueprintIdVersionRange));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BlueprintIdVersionRange value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public override BlueprintIdVersionRange ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.PropertyName
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(BlueprintIdVersionRange), reader.TokenType, nameof(JsonTokenType.PropertyName));

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return !string.IsNullOrEmpty(str) && str != null
            ? new BlueprintIdVersionRange(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(BlueprintIdVersionRange));
    }

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, BlueprintIdVersionRange value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.FullName);
    }
}
