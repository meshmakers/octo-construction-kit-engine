using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Type converter for System.Text.Json for <see cref="CkVersion"/>
/// </summary>
public class CkVersionConverter: JsonConverter<CkVersion>
{
    /// <inheritdoc />
    public override CkVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : throw ModelParseException.UnexpectedToken(nameof(CkVersion), reader.TokenType, nameof(JsonTokenType.String));
        return !string.IsNullOrEmpty(str) && str != null
            ? new CkVersion(str)
            : throw ModelParseException.ValueCannotBeEmpty(nameof(CkVersion));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CkVersion value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}