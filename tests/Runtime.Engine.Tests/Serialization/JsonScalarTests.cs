using System.Text.Json;
using System.Text.Json.Nodes;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

/// <summary>
/// Tests for <see cref="JsonScalar.ToClr"/> — the single-source Newtonsoft-parity scalar-boxing
/// primitive extracted from <see cref="RtAttributesConverter"/>.
/// </summary>
public class JsonScalarTests
{
    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    // ──────────────────────────────────────────────────────────────────
    // JsonScalar.ToClr(JsonElement)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ToClr_SmallInteger_ReturnsInt()
    {
        // Matches Newtonsoft's in-memory round-trip behaviour (JObject.FromObject(int) → JValue
        // with Value=Int32). Enforced by Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests.
        var result = JsonScalar.ToClr(Parse("42"));
        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ToClr_LargeInteger_ReturnsLong()
    {
        // Values that don't fit in Int32 fall through to Int64.
        var result = JsonScalar.ToClr(Parse("2147483648")); // int.MaxValue + 1
        Assert.IsType<long>(result);
        Assert.Equal(2147483648L, result);
    }

    [Fact]
    public void ToClr_Real_ReturnsDouble()
    {
        var result = JsonScalar.ToClr(Parse("3.14"));
        Assert.IsType<double>(result);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ToClr_IntegerValuedReal_ReturnsDouble()
    {
        // 2.0 has a decimal point so TryGetInt64 fails → must be double, not long.
        var result = JsonScalar.ToClr(Parse("2.0"));
        Assert.IsType<double>(result);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void ToClr_Iso8601String_ParseDatesTrue_ReturnsDateTime()
    {
        var result = JsonScalar.ToClr(Parse("\"2024-01-15T10:30:00\""), parseDateStrings: true);
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result);
    }

    [Fact]
    public void ToClr_Iso8601String_ParseDatesFalse_ReturnsString()
    {
        var result = JsonScalar.ToClr(Parse("\"2024-01-15T10:30:00\""), parseDateStrings: false);
        Assert.IsType<string>(result);
        Assert.Equal("2024-01-15T10:30:00", (string)result!);
    }

    [Fact]
    public void ToClr_NonDateString_ReturnsString()
    {
        var result = JsonScalar.ToClr(Parse("\"hello world\""));
        Assert.IsType<string>(result);
        Assert.Equal("hello world", (string)result!);
    }

    [Fact]
    public void ToClr_BoolTrue_ReturnsBool()
    {
        var result = JsonScalar.ToClr(Parse("true"));
        Assert.IsType<bool>(result);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ToClr_BoolFalse_ReturnsBool()
    {
        var result = JsonScalar.ToClr(Parse("false"));
        Assert.IsType<bool>(result);
        Assert.Equal(false, result);
    }

    [Fact]
    public void ToClr_Null_ReturnsNull()
    {
        var result = JsonScalar.ToClr(Parse("null"));
        Assert.Null(result);
    }

    [Fact]
    public void ToClr_Object_ReturnsNull()
    {
        var result = JsonScalar.ToClr(Parse("""{"x":1}"""));
        Assert.Null(result);
    }

    [Fact]
    public void ToClr_Array_ReturnsNull()
    {
        var result = JsonScalar.ToClr(Parse("[1,2,3]"));
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // JsonScalar.ToClr(JsonValue) — CLR-backed values
    //
    // A JsonValue is element-backed (parsed JSON / SerializeToNode) or CLR-backed
    // (JsonValue.Create(primitive) and its DeepClone — what pipeline nodes produce when they
    // author a scalar, e.g. TransformStringNode's Set($.Operator, JsonValue.Create(substring))).
    // GetValue<JsonElement>() throws on the latter, so ToClr must unwrap by kind. Boxing must
    // match the element path / Newtonsoft (string→string, int→Int32, long→Int64, real→double,
    // bool→bool, ISO-string→DateTime).
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ToClr_ClrBackedString_ReturnsString()
    {
        var result = JsonScalar.ToClr(JsonValue.Create("DAR")!);
        Assert.IsType<string>(result);
        Assert.Equal("DAR", result);
    }

    [Fact]
    public void ToClr_ClrBackedSmallInteger_ReturnsInt()
    {
        var result = JsonScalar.ToClr(JsonValue.Create(42)!);
        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ToClr_ClrBackedLargeInteger_ReturnsLong()
    {
        var result = JsonScalar.ToClr(JsonValue.Create(2147483648L)!); // int.MaxValue + 1
        Assert.IsType<long>(result);
        Assert.Equal(2147483648L, result);
    }

    [Fact]
    public void ToClr_ClrBackedIntegerValuedReal_StaysDouble()
    {
        // THE CRUX: a CLR-backed double 2.0 must box to double, NOT collapse to int — otherwise
        // we'd reintroduce the Int32/Int64-vs-double (BsonInt64) regression class.
        var result = JsonScalar.ToClr(JsonValue.Create(2.0)!);
        Assert.IsType<double>(result);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void ToClr_ClrBackedReal_ReturnsDouble()
    {
        var result = JsonScalar.ToClr(JsonValue.Create(3.14)!);
        Assert.IsType<double>(result);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ToClr_ClrBackedDecimal_ReturnsDouble()
    {
        // Reals→double parity (decimal vs double is a documented irreducible divergence; the
        // element path likewise yields double for the same numeric token).
        var result = JsonScalar.ToClr(JsonValue.Create(2.5m)!);
        Assert.IsType<double>(result);
        Assert.Equal(2.5, result);
    }

    [Fact]
    public void ToClr_ClrBackedBool_ReturnsBool()
    {
        var result = JsonScalar.ToClr(JsonValue.Create(true)!);
        Assert.IsType<bool>(result);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ToClr_ClrBackedIso8601String_ParseDatesTrue_ReturnsDateTime()
    {
        var result = JsonScalar.ToClr(JsonValue.Create("2024-01-15T10:30:00")!, parseDateStrings: true);
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result);
    }

    [Fact]
    public void ToClr_ClrBackedIso8601String_ParseDatesFalse_ReturnsString()
    {
        var result = JsonScalar.ToClr(JsonValue.Create("2024-01-15T10:30:00")!, parseDateStrings: false);
        Assert.IsType<string>(result);
        Assert.Equal("2024-01-15T10:30:00", (string)result!);
    }

    [Fact]
    public void ToClr_ClrBackedNonDateString_ReturnsString()
    {
        var result = JsonScalar.ToClr(JsonValue.Create("hello world")!);
        Assert.IsType<string>(result);
        Assert.Equal("hello world", (string)result!);
    }

    [Fact]
    public void ToClr_ElementBackedJsonValue_Unchanged()
    {
        // Regression guard: element-backed JsonValues (the existing fast path) still box correctly
        // via the JsonValue overload — small int → Int32, real → double.
        Assert.IsType<int>(JsonScalar.ToClr((JsonValue)JsonNode.Parse("42")!));
        Assert.Equal(42, JsonScalar.ToClr((JsonValue)JsonNode.Parse("42")!));
        Assert.IsType<double>(JsonScalar.ToClr((JsonValue)JsonNode.Parse("2.0")!));
        Assert.Equal(2.0, JsonScalar.ToClr((JsonValue)JsonNode.Parse("2.0")!));
        Assert.Equal("DAR", JsonScalar.ToClr((JsonValue)JsonNode.Parse("\"DAR\"")!));
    }

    // ──────────────────────────────────────────────────────────────────
    // JsonScalar.TryToNumber<T>
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryToNumber_JsonNumber_ReturnsTrue()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("3.5")!;
        var result = JsonScalar.TryToNumber<double>(node, out var value);
        Assert.True(result);
        Assert.Equal(3.5, value);
    }

    [Fact]
    public void TryToNumber_NumericString_ReturnsTrue()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("\"3.5\"")!;
        var result = JsonScalar.TryToNumber<double>(node, out var value);
        Assert.True(result);
        Assert.Equal(3.5, value);
    }

    [Fact]
    public void TryToNumber_NonNumericString_ReturnsFalse()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("\"banana\"")!;
        var result = JsonScalar.TryToNumber<double>(node, out _);
        Assert.False(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // JsonScalar.TryToDouble (non-generic; available on netstandard2.0)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryToDouble_JsonNumber_ReturnsTrue()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("3.5")!;
        var result = JsonScalar.TryToDouble(node, out var value);
        Assert.True(result);
        Assert.Equal(3.5, value);
    }

    [Fact]
    public void TryToDouble_NumericString_ReturnsTrue()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("\"3.5\"")!;
        var result = JsonScalar.TryToDouble(node, out var value);
        Assert.True(result);
        Assert.Equal(3.5, value);
    }

    [Fact]
    public void TryToDouble_NonNumericString_ReturnsFalse()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("\"banana\"")!;
        var result = JsonScalar.TryToDouble(node, out _);
        Assert.False(result);
    }
}
