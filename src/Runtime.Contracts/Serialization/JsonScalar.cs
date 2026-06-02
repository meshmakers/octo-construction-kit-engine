using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// Converts a scalar JSON value to its Newtonsoft-parity CLR boxing — the single source
/// of the rules previously hand-rolled across many pipeline nodes and inside
/// <see cref="RtAttributesConverter"/>. Integers that fit in <see cref="int"/> box to
/// <see cref="int"/>, larger integers to <see cref="long"/>, reals to <see cref="double"/>,
/// ISO-8601 strings to <see cref="DateTime"/> (when requested), bools to <see cref="bool"/>;
/// objects/arrays return null (callers navigate those structurally).
/// </summary>
/// <remarks>
/// The boxing rules are verified empirically against Newtonsoft by
/// <c>Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests</c>. That suite is
/// the authoritative contract — any divergence between these rules and what Newtonsoft's
/// <c>JObject.FromObject</c> / <c>JToken.ToObject</c> in-memory round-trip produces is a
/// regression. Irreducible divergences (float vs double, decimal vs double, DateTimeOffset
/// vs DateTime — lost because JSON has no source-CLR-type marker for those) are listed in
/// <c>AttributeValueParityCorpus.IrreducibleDivergences</c>.
/// </remarks>
public static class JsonScalar
{
    /// <summary>Newtonsoft-parity scalar boxing of <paramref name="element"/>.</summary>
    public static object? ToClr(JsonElement element, bool parseDateStrings = true)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return parseDateStrings && element.TryGetDateTime(out var dt) ? dt : element.GetString();
            case JsonValueKind.Number:
                // Prefer Int32 (matches Newtonsoft's JObject.FromObject(int) → JValue with
                // Value=Int32). Falling through to Int64 only when the value doesn't fit in Int32
                // keeps large ids and unix-ms-style values intact. Explicit if/return, NOT a
                // ternary: a `long : double` conditional has common type double and would widen
                // every integer to double before boxing.
                if (element.TryGetInt32(out var i)) return i;
                if (element.TryGetInt64(out var l)) return l;
                return element.GetDouble();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            default:
                return null; // Object / Array / Null / Undefined
        }
    }

    /// <summary>Newtonsoft-parity scalar boxing of a <see cref="JsonValue"/>.</summary>
    /// <remarks>
    /// A <see cref="JsonValue"/> is either element-backed (parsed JSON / <c>Deserialize&lt;JsonNode&gt;</c>
    /// / <c>SerializeToNode</c> of a boxed primitive) or CLR-backed (<c>JsonValue.Create(primitive)</c>
    /// and its <c>DeepClone</c> — what pipeline nodes produce when they author a scalar, e.g.
    /// <c>TransformStringNode</c>'s <c>Set(path, JsonValue.Create(result))</c>, surviving the overlay
    /// store and detach). Only the element-backed form can be read via <c>GetValue&lt;JsonElement&gt;()</c>;
    /// a CLR-backed value throws <see cref="InvalidOperationException"/> there. So element-backed values
    /// route through the proven <see cref="ToClr(JsonElement, bool)"/>, and CLR-backed values are unwrapped
    /// by kind with the SAME Newtonsoft-parity boxing (string→string, ISO-string→DateTime, Int32 then
    /// Int64 then Double, bool→bool). The Number arm tries Int32 before Int64 before Double because STJ's
    /// <c>TryGetValue&lt;T&gt;</c> on a CLR-backed value is exact-type (no numeric coercion): a boxed
    /// <c>double 2.0</c> fails <c>TryGetValue&lt;int&gt;</c> and stays a <see cref="double"/> rather than
    /// collapsing to <see cref="int"/> (the Int32-vs-double regression the parity converters guard against).
    /// </remarks>
    public static object? ToClr(JsonValue value, bool parseDateStrings = true)
    {
        // Element-backed fast path — bit-for-bit identical to the prior behaviour.
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return ToClr(element, parseDateStrings);
        }

        // CLR-backed (JsonValue.Create(...) / DeepClone): GetValue<JsonElement>() would throw.
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                // Route the string through an element so ISO-8601 date detection is IDENTICAL to the
                // JsonElement overload (element.TryGetDateTime), not an approximate DateTime.TryParse.
                return ToClr(JsonSerializer.SerializeToElement(value.GetValue<string>()), parseDateStrings);
            case JsonValueKind.Number:
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<long>(out var l)) return l;
                if (value.TryGetValue<double>(out var d)) return d;
                if (value.TryGetValue<decimal>(out var m)) return (double)m; // reals → double (parity)
                return null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return value.GetValue<bool>();
            default:
                return null; // Object / Array / Null / Undefined — callers navigate structurally
        }
    }

#if NET7_0_OR_GREATER
    /// <summary>
    /// Reads <paramref name="node"/> as numeric <typeparamref name="T"/>. Accepts JSON numbers
    /// natively; parses JSON strings under invariant culture. Returns false otherwise.
    /// </summary>
    public static bool TryToNumber<T>(JsonNode node, out T value)
        where T : struct, IParsable<T>
    {
        try
        {
            value = node.GetValue<T>();
            return true;
        }
        catch (FormatException)
        {
            value = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            if (node is JsonValue jv
                && jv.TryGetValue<string>(out var s)
                && T.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
            value = default;
            return false;
        }
    }
#endif

    /// <summary>
    /// Reads <paramref name="node"/> as a <see cref="double"/> — the non-generic counterpart of
    /// <c>TryToNumber&lt;T&gt;</c>. Accepts JSON numbers natively and parses numeric JSON strings
    /// under invariant culture; returns false otherwise. Unlike the generic overload (which needs
    /// <c>IParsable&lt;T&gt;</c>, net7+), this is available on every target framework — including
    /// netstandard2.0 — so callers that cannot use the generic still get the shared parity rules.
    /// </summary>
    public static bool TryToDouble(JsonNode node, out double value)
    {
        try
        {
            value = node.GetValue<double>();
            return true;
        }
        catch (FormatException)
        {
            value = default;
            return false;
        }
        catch (InvalidOperationException)
        {
            if (node is JsonValue jv
                && jv.TryGetValue<string>(out var s)
                && double.TryParse(s,
                    System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
            value = default;
            return false;
        }
    }
}
