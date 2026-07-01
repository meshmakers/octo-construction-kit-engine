using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class AttributeValueConverterTests
{
    [Theory]
    [InlineData("hello ", "hello")]
    [InlineData(" hello", "hello")]
    [InlineData(" hello ", "hello")]
    [InlineData("  hello  world  ", "hello  world")]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    public void ConvertAttributeValue_String_TrimsWhitespace(string input, string expected)
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.String, input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertAttributeValue_String_Null_ReturnsNull()
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.String, null);

        Assert.Null(result);
    }

    [Fact]
    public void ConvertAttributeValue_String_NonStringValue_TrimsConvertedResult()
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.String, 42);

        Assert.Equal("42", result);
    }

    [Fact]
    public void ConvertAttributeValue_StringArray_TrimsAllElements()
    {
        var input = new[] { " hello ", "world ", " test" };

        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.StringArray, input);

        var list = Assert.IsType<List<string>>(result);
        Assert.Equal(["hello", "world", "test"], list);
    }

    [Fact]
    public void ConvertAttributeValue_StringArray_AlreadyTrimmed_Unchanged()
    {
        var input = new[] { "hello", "world" };

        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.StringArray, input);

        var list = Assert.IsType<List<string>>(result);
        Assert.Equal(["hello", "world"], list);
    }

    [Fact]
    public void SetAttributeValue_String_TrimsWhitespace()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValue("Name", AttributeValueTypesDto.String, "  test value  ");

        var result = rtEntity.GetAttributeStringValueOrDefault("Name");

        Assert.Equal("test value", result);
    }

    [Fact]
    public void SetAttributeValueNonNullable_String_TrimsWhitespace()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("Name", AttributeValueTypesDto.String, " trimmed ");

        var result = rtEntity.GetAttributeStringValue("Name");

        Assert.Equal("trimmed", result);
    }

    [Fact]
    public void SetAttributeValue_StringArray_TrimsAllElements()
    {
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("Tags", AttributeValueTypesDto.StringArray,
            new[] { " tag1 ", "tag2 ", " tag3" });

        var result = rtEntity.GetAttributeStringValues("Tags");

        Assert.Equal(3, result.Count);
        Assert.Equal("tag1", result[0]);
        Assert.Equal("tag2", result[1]);
        Assert.Equal("tag3", result[2]);
    }

    [Fact]
    public void ConvertAttributeValue_TimeSpan_PassesThroughTimeSpan()
    {
        var input = TimeSpan.FromMinutes(15);

        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.TimeSpan, input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void ConvertAttributeValue_TimeSpan_BareTicksString_ParsedAsTicks()
    {
        // AB#4259: the ImportRt export/import JSON round-trip persists a TimeSpan attribute as a
        // bare-integer ticks string. It must be read back as ticks, not handed to TimeSpan.Parse
        // (which would read it as 9-billion days and overflow).
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.TimeSpan, "9000000000");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void ConvertAttributeValue_TimeSpan_BareTicksLong_ParsedAsTicks()
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.TimeSpan, 9000000000L);

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Fact]
    public void ConvertAttributeValue_TimeSpan_DotNetString_Parsed()
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.TimeSpan, "00:15:00");

        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    // ---- AB#4281: Int64 attribute (RollupArchive.BucketSizeMs / WatermarkLagMs) ------------------
    // A >Int32 integer for an Int64 attribute must persist as a number (long), never a string, so the
    // subsequent read (Convert.ChangeType to long) never throws the "too large for Int32" overflow.

    [Theory]
    [InlineData(2_419_200_000L)]   // calendar-month bucket width in ms
    [InlineData(31_536_000_000L)]  // calendar-year bucket width in ms
    public void ConvertAttributeValue_Int64_LongBeyondInt32_PassesThroughAsLong(long input)
    {
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Int64, input);

        Assert.IsType<long>(result);
        Assert.Equal(input, result);
    }

    [Fact]
    public void ConvertAttributeValue_Int64_Int32Value_WidensToLong()
    {
        // Existing (pre-1.6.3) rows stored the value as a boxed Int32; it must still load as Int64.
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Int64, 300_000);

        Assert.IsType<long>(result);
        Assert.Equal(300_000L, result);
    }

    [Fact]
    public void ConvertAttributeValue_Int64_BareString_ParsedAsLong()
    {
        // The ImportRt export/import round-trip can deliver the value as a bare string. A >Int32
        // string must parse to long, not throw the Int32 overflow that motivated AB#4281/AB#4282.
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Int64, "2419200000");

        Assert.IsType<long>(result);
        Assert.Equal(2_419_200_000L, result);
    }

    [Fact]
    public void ConvertAttributeValue_Int64_JsonNumberElement_ParsedAsLong()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("31536000000");
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Int64, doc.RootElement);

        Assert.IsType<long>(result);
        Assert.Equal(31_536_000_000L, result);
    }

    [Fact]
    public void ConvertAttributeValue_Int64_JsonStringElement_ParsedAsLong()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("\"2419200000\"");
        var result = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Int64, doc.RootElement);

        Assert.IsType<long>(result);
        Assert.Equal(2_419_200_000L, result);
    }

    [Fact]
    public void Int64Attribute_RoundTripsBeyondInt32_WithoutOverflow()
    {
        // End-to-end at the entity level: the setter stores it via ConvertAttributeValue(Int64) and
        // the generated-style read (GetAttributeValue<long>) returns it — no Int32 overflow. This is
        // the read path that previously threw OverflowException on a calendar rollup (AB#4282).
        var rtEntity = new RtEntity();
        rtEntity.SetAttributeValueNonNullable("BucketSizeMs", AttributeValueTypesDto.Int64, 2_419_200_000L);

        var result = rtEntity.GetAttributeValue<long>("BucketSizeMs");

        Assert.Equal(2_419_200_000L, result);
    }
}
