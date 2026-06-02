using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// System.Text.Json converters that match Newtonsoft's whole-number formatting for
/// <see cref="double"/>, <see cref="float"/>, and <see cref="decimal"/>: an integral
/// value renders with a trailing <c>.0</c> (e.g. <c>0.0</c> rather than STJ's default
/// <c>0</c>).
/// </summary>
/// <remarks>
/// <para>
/// Without these converters STJ writes <c>double 0.0</c> as the JSON literal <c>0</c>,
/// which on round-trip through <see cref="RtAttributesConverter"/> + <see cref="JsonScalar"/>
/// looks like an integer and gets boxed as <see cref="long"/>. That widening then surfaces in
/// MongoDB as a <c>BsonInt64</c> attribute where Newtonsoft's path stored a <c>BsonDouble</c> —
/// observable as the <c>(quantity=0, BsonInt64)</c> rows in
/// <c>RtEntity_EnergyCommunityEnergyQuantity</c> on the octogrid tenant after the STJ migration.
/// </para>
/// <para>
/// Newtonsoft's <c>JsonConvert.ToString(double 0.0)</c> emits <c>"0.0"</c> (its writer always
/// appends a fractional digit to disambiguate from integers). These converters reproduce that
/// rule. Non-integral, non-special values fall through to the built-in number writer / reader,
/// so precision and round-tripping behaviour for normal fractional values is unchanged.
/// </para>
/// <para>
/// <b>NumberHandling flag preservation.</b> Custom converters bypass STJ's built-in number
/// handling, so the converter must re-implement <see cref="JsonNumberHandling"/> behaviour
/// for the flags <see cref="RtSystemTextJsonSerializer"/> sets:
/// </para>
/// <list type="bullet">
///   <item><see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/> — NaN / Infinity /
///         -Infinity written as JSON strings on write, accepted as strings on read.</item>
///   <item><see cref="JsonNumberHandling.AllowReadingFromString"/> — numeric JSON strings
///         (<c>"42"</c>, <c>"3.14"</c>) accepted on read in addition to bare numbers.</item>
/// </list>
/// <para>
/// Implementation uses <see cref="Utf8Formatter"/> with a <c>stackalloc Span&lt;byte&gt;</c> for
/// the integral path — no heap allocations per value.
/// </para>
/// <para>
/// Verified by
/// <c>Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests</c>: with these
/// converters registered, <c>double-zero</c> and <c>double-one</c> round-trip as
/// <see cref="double"/> matching Newtonsoft. <c>decimal</c> and <c>float</c> sources reach the
/// boundary but still lose source-CLR-type fidelity (JSON has no decimal-vs-double or
/// float-vs-double marker) — those are documented in
/// <c>AttributeValueParityCorpus.IrreducibleDivergences</c>; the integral-<c>.0</c> emission
/// still prevents the <c>long</c>-widening for those whole-number cases.
/// </para>
/// </remarks>
public sealed class NewtonsoftParityDoubleConverter : JsonConverter<double>
{
    /// <inheritdoc />
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0)
        {
            var s = reader.GetString();
            if (s == null) return 0d;
            if ((options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            {
                if (s == "NaN") return double.NaN;
                if (s == "Infinity") return double.PositiveInfinity;
                if (s == "-Infinity") return double.NegativeInfinity;
            }
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return reader.GetDouble();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            if ((options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            {
                writer.WriteStringValue(
                    double.IsNaN(value) ? "NaN"
                    : double.IsPositiveInfinity(value) ? "Infinity"
                    : "-Infinity");
                return;
            }
            // Let STJ throw the standard ArgumentException so behaviour matches non-converter case.
            writer.WriteNumberValue(value);
            return;
        }

        if (value == Math.Truncate(value))
        {
            // Integral and finite. STJ would emit "0" for double 0.0; Newtonsoft emits "0.0".
            // Format to a stack buffer (32 bytes is enough for any double's "R" form), then
            // ensure a trailing ".0" before emitting as a raw JSON number.
            Span<byte> buffer = stackalloc byte[32];
            if (Utf8Formatter.TryFormat(value, buffer, out var written, new StandardFormat('R')))
            {
                if (HasFractionalOrExponent(buffer.Slice(0, written)))
                {
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                else
                {
                    buffer[written++] = (byte)'.';
                    buffer[written++] = (byte)'0';
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                return;
            }
            // Fallback for the (effectively impossible) buffer-too-small case.
        }

        writer.WriteNumberValue(value);
    }

    internal static bool HasFractionalOrExponent(ReadOnlySpan<byte> utf8Number)
    {
        // 'R' format includes '.' for non-integer values, 'E' / 'e' for scientific notation
        // (e.g. 1E+20 — Newtonsoft writes the same shape in that range, so leave it as-is).
        for (var i = 0; i < utf8Number.Length; i++)
        {
            var b = utf8Number[i];
            if (b == (byte)'.' || b == (byte)'E' || b == (byte)'e')
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>Single-precision twin of <see cref="NewtonsoftParityDoubleConverter"/>.</summary>
public sealed class NewtonsoftParitySingleConverter : JsonConverter<float>
{
    /// <inheritdoc />
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0)
        {
            var s = reader.GetString();
            if (s == null) return 0f;
            if ((options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            {
                if (s == "NaN") return float.NaN;
                if (s == "Infinity") return float.PositiveInfinity;
                if (s == "-Infinity") return float.NegativeInfinity;
            }
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return reader.GetSingle();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            if ((options.NumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
            {
                writer.WriteStringValue(
                    float.IsNaN(value) ? "NaN"
                    : float.IsPositiveInfinity(value) ? "Infinity"
                    : "-Infinity");
                return;
            }
            writer.WriteNumberValue(value);
            return;
        }

        if (value == (float)Math.Truncate(value))
        {
            Span<byte> buffer = stackalloc byte[16];
            if (Utf8Formatter.TryFormat(value, buffer, out var written, new StandardFormat('R')))
            {
                if (NewtonsoftParityDoubleConverter.HasFractionalOrExponent(buffer.Slice(0, written)))
                {
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                else
                {
                    buffer[written++] = (byte)'.';
                    buffer[written++] = (byte)'0';
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                return;
            }
        }

        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// <see cref="decimal"/> twin of <see cref="NewtonsoftParityDoubleConverter"/>. Whole-number
/// decimals render with a trailing <c>.0</c> so the round-trip lands as a JSON real (and thus
/// boxes as <see cref="double"/>) rather than a JSON integer (which would box as <see cref="long"/>).
/// Note: decimal-vs-double parity itself is irreducible — JSON has no decimal marker.
/// </summary>
public sealed class NewtonsoftParityDecimalConverter : JsonConverter<decimal>
{
    /// <inheritdoc />
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            (options.NumberHandling & JsonNumberHandling.AllowReadingFromString) != 0)
        {
            var s = reader.GetString();
            return s == null ? 0m : decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
        }
        return reader.GetDecimal();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        if (value == Math.Truncate(value))
        {
            Span<byte> buffer = stackalloc byte[64];
            if (Utf8Formatter.TryFormat(value, buffer, out var written))
            {
                if (NewtonsoftParityDoubleConverter.HasFractionalOrExponent(buffer.Slice(0, written)))
                {
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                else
                {
                    buffer[written++] = (byte)'.';
                    buffer[written++] = (byte)'0';
                    writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
                }
                return;
            }
        }

        writer.WriteNumberValue(value);
    }
}
