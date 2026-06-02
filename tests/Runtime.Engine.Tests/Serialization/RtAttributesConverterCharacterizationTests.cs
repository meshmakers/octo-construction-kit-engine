using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Serialization;

/// <summary>
/// Characterization tests that pin the CURRENT scalar-boxing rules of
/// <see cref="RtAttributesConverter"/> as exercised via <see cref="RtSystemTextJsonSerializer.Default"/>.
///
/// These tests describe observed behavior and MUST continue to pass after every refactor.
/// If a test needs to change it means the boxing rules changed, which is a breaking change.
/// </summary>
public class RtAttributesConverterCharacterizationTests
{
    /// <summary>
    /// Deserializes <c>{"CkTypeId":"Test/T","RtId":"","Attributes":{"a":<raw>}}</c> and returns
    /// the boxed CLR value of attribute "a".
    /// </summary>
    private static object? RoundTripAttribute(string rawJsonValue)
    {
        var json = $$$"""{"CkTypeId":"Test/T","RtId":"","Attributes":{"a":{{{rawJsonValue}}}}}""";
        var entity = JsonSerializer.Deserialize<RtEntity>(json, RtSystemTextJsonSerializer.Default);
        Assert.NotNull(entity);
        Assert.True(entity.Attributes.TryGetValue("a", out var value),
            $"Attribute 'a' was not present in deserialized entity. JSON: {json}");
        return value;
    }

    [Fact]
    public void SmallInteger_BoxesToInt()
    {
        // Newtonsoft-parity boxing: ints that fit in Int32 stay Int32 (matches JObject.FromObject(int)).
        // Enforced by Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests.
        var result = RoundTripAttribute("42");
        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void LargeInteger_BoxesToLong()
    {
        // Values that don't fit in Int32 fall through to Int64.
        var result = RoundTripAttribute("2147483648");
        Assert.IsType<long>(result);
        Assert.Equal(2147483648L, result);
    }

    [Fact]
    public void Real_BoxesToDouble()
    {
        var result = RoundTripAttribute("3.14");
        Assert.IsType<double>(result);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void IntegerValuedReal_BoxesToDouble()
    {
        // "2.0" is a JSON number with a decimal point — must become double, not long.
        var result = RoundTripAttribute("2.0");
        Assert.IsType<double>(result);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void Iso8601String_BoxesToDateTime()
    {
        var result = RoundTripAttribute("\"2024-01-15T10:30:00\"");
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2024, 1, 15, 10, 30, 0), result);
    }

    [Fact]
    public void NonDateString_BoxesToString()
    {
        var result = RoundTripAttribute("\"hello world\"");
        Assert.IsType<string>(result);
        Assert.Equal("hello world", (string)result!);
    }

    [Fact]
    public void BoolTrue_BoxesToBool()
    {
        var result = RoundTripAttribute("true");
        Assert.IsType<bool>(result);
        Assert.Equal(true, result);
    }

    [Fact]
    public void BoolFalse_BoxesToBool()
    {
        var result = RoundTripAttribute("false");
        Assert.IsType<bool>(result);
        Assert.Equal(false, result);
    }

    [Fact]
    public void Null_BoxesToNull()
    {
        // NOTE: RtSystemTextJsonSerializer.Default uses WhenWritingNull, so null attributes
        // may be dropped on serialize. But on deserialize, an explicit JSON null becomes null.
        var json = """{"CkTypeId":"Test/T","RtId":"","Attributes":{"a":null}}""";
        var entity = JsonSerializer.Deserialize<RtEntity>(json, RtSystemTextJsonSerializer.Default);
        Assert.NotNull(entity);
        // The attribute may be stored as null or absent; if present it must be null.
        if (entity.Attributes.TryGetValue("a", out var value))
        {
            Assert.Null(value);
        }
        // If absent (key dropped because value was null), that is also acceptable behavior.
    }
}
