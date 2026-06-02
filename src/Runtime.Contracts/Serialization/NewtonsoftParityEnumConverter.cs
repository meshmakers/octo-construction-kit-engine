using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// System.Text.Json enum converter that matches Newtonsoft's enum wire form: a plain CLR enum is
/// WRITTEN as its underlying <b>integer</b> (Newtonsoft's serializer has no <c>StringEnumConverter</c>),
/// and is READ from EITHER the integer OR the member-name string (Newtonsoft's reader is lenient).
/// </summary>
/// <remarks>
/// <para>
/// This is the enum sibling of <see cref="NewtonsoftParityDoubleConverter"/> /
/// <see cref="NewtonsoftParitySingleConverter"/> / <see cref="NewtonsoftParityDecimalConverter"/>: all
/// exist purely so <see cref="RtSystemTextJsonSerializer"/> produces the same bytes as its Newtonsoft
/// twin <c>RtNewtonsoftSerializer</c>. It replaces a <see cref="JsonStringEnumConverter"/> that emitted
/// the member NAME — a parity regression: <c>SystemTextJsonOptions.Default</c> (octo-sdk) derives from
/// this bundle and feeds the ETL pipeline DataContext, so a CLR enum (e.g. the EDA adapter's
/// <c>EnergyDirection</c>) surfaced as <c>"Consumption"</c> instead of <c>1</c>, breaking pipeline
/// consumers that read it as <c>Int</c> (<c>DataMapping@1 sourceValueType: Int</c>, <c>If@1</c>/<c>Switch@1
/// valueType: Int</c>).
/// </para>
/// <para>
/// Write emits the underlying integer; read accepts a number, a member-name string (case-insensitive),
/// or a numeric string — keeping the historical Newtonsoft read tolerance so externally-supplied
/// name-form payloads (e.g. inbound EDA process data) still deserialize.
/// </para>
/// </remarks>
public sealed class NewtonsoftParityEnumConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(EnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class EnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly bool UnsignedLong =
            Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) == TypeCode.UInt64;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    // Read the underlying integer and map it onto the enum (Enum.ToObject handles any
                    // integral underlying type). Newtonsoft accepted bare numbers for enums.
                    return (T)Enum.ToObject(typeof(T), reader.GetInt64());
                case JsonTokenType.String:
                    // Newtonsoft read tolerance: accept the member name (case-insensitive) or a numeric
                    // string (Enum.TryParse handles both forms).
                    var s = reader.GetString();
                    if (s != null && Enum.TryParse<T>(s, ignoreCase: true, out var parsed))
                    {
                        return parsed;
                    }

                    throw new JsonException($"Cannot convert \"{s}\" to enum {typeof(T).Name}.");
                default:
                    throw new JsonException(
                        $"Unexpected token {reader.TokenType} when reading enum {typeof(T).Name}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // Newtonsoft parity: emit the underlying integer, never the member name.
            if (UnsignedLong)
            {
                writer.WriteNumberValue(Convert.ToUInt64(value));
            }
            else
            {
                writer.WriteNumberValue(Convert.ToInt64(value));
            }
        }
    }
}
