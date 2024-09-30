using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
///    Converter for System.Text.Json and YamlDotNet for <see cref="ICollection{Object}" />/>
/// </summary>
public class DefaultValuesConverter: JsonConverter<ICollection<object>>
{
    /// <inheritdoc />
    public override ICollection<object>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw ModelParseException.UnexpectedToken(nameof(ICollection<object>), reader.TokenType, nameof(JsonTokenType.StartArray));
        }

        var list = new List<object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return list;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt16(out var i16))
                {
                    list.Add(i16);
                }
                else if (reader.TryGetInt32(out var i32))
                {
                    list.Add(i32);
                }
                else if (reader.TryGetInt64(out var i64))
                {
                    list.Add(i64);
                }
                else if (reader.TryGetSingle(out var f32))
                {
                    list.Add(f32);
                }
                else if (reader.TryGetDouble(out var f64))
                {
                    list.Add(f64);
                }
            }
            else if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            {
                list.Add(reader.GetBoolean());
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (s != null)
                {
                    list.Add(s);
                }
            }
        }

        throw new JsonException();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ICollection<object> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            if (item is short i16)
            {
                writer.WriteNumberValue(i16);
            }
            else if (item is int i32)
            {
                writer.WriteNumberValue(i32);
            }
            else if (item is long i64)
            {
                writer.WriteNumberValue(i64);
            }
            else if (item is float f32)
            {
                writer.WriteNumberValue(f32);
            }
            else if (item is double f64)
            {
                writer.WriteNumberValue(f64);
            }
            else if (item is bool b)
            {
                writer.WriteBooleanValue(b);
            }
            else
            {
                writer.WriteStringValue(item.ToString());
            }
        }
        writer.WriteEndArray();
    }
}