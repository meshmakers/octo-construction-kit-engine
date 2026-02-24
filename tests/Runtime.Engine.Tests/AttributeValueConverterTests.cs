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
}
