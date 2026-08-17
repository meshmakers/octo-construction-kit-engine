using Meshmakers.Octo.ConstructionKit.Contracts.DisplayRules;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DisplayRules;

public class DisplayRuleParserTests
{
    [Fact]
    public void Parse_LiteralAndPlaceholders_SegmentsInOrder()
    {
        var result = DisplayRuleParser.Parse("${roomNumber} - ${name}");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Segments.Count);
        var placeholder1 = Assert.IsType<DisplayRulePlaceholderSegment>(result.Segments[0]);
        Assert.Equal(["roomNumber"], placeholder1.Paths);
        var literal = Assert.IsType<DisplayRuleLiteralSegment>(result.Segments[1]);
        Assert.Equal(" - ", literal.Text);
        var placeholder2 = Assert.IsType<DisplayRulePlaceholderSegment>(result.Segments[2]);
        Assert.Equal(["name"], placeholder2.Paths);
    }

    [Fact]
    public void Parse_CoalesceChain_AllPathsCaptured()
    {
        var result = DisplayRuleParser.Parse("${name ?? globalId ?? roomNumber}");

        Assert.True(result.IsValid);
        var placeholder = Assert.IsType<DisplayRulePlaceholderSegment>(Assert.Single(result.Segments));
        Assert.Equal(["name", "globalId", "roomNumber"], placeholder.Paths);
    }

    [Fact]
    public void Parse_RecordPath_IsValid()
    {
        var result = DisplayRuleParser.Parse("${thermalRequirements.spaceTemperature}");

        Assert.True(result.IsValid);
        Assert.Equal(["thermalRequirements.spaceTemperature"], result.ReferencedPaths);
    }

    [Fact]
    public void Parse_ReferencedPaths_AreDistinctInFirstOccurrenceOrder()
    {
        var result = DisplayRuleParser.Parse("${b ?? a} ${a} ${c}");

        Assert.True(result.IsValid);
        Assert.Equal(["b", "a", "c"], result.ReferencedPaths);
    }

    [Fact]
    public void Parse_UnterminatedPlaceholder_Error()
    {
        var result = DisplayRuleParser.Parse("${name");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Unterminated"));
    }

    [Fact]
    public void Parse_EmptyPlaceholder_Error()
    {
        var result = DisplayRuleParser.Parse("${}");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_InvalidPathSyntax_Error()
    {
        var result = DisplayRuleParser.Parse("${name-with-dash}");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid attribute path"));
    }

    [Fact]
    public void Parse_EmptyCoalesceOperand_Error()
    {
        var result = DisplayRuleParser.Parse("${name ??}");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Empty attribute path"));
    }

    [Fact]
    public void Parse_EmptyRule_Error()
    {
        var result = DisplayRuleParser.Parse("");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_LiteralOnlyRule_Error()
    {
        var result = DisplayRuleParser.Parse("Fixed name");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no ${attributePath} placeholder"));
    }

    [Fact]
    public void Evaluate_ConcatenatesLiteralsAndValues()
    {
        var result = DisplayRuleParser.Parse("${roomNumber} - ${name}");
        var values = new Dictionary<string, object?> { ["roomNumber"] = "EG01", ["name"] = "Wohnbereich" };

        Assert.Equal("EG01 - Wohnbereich", result.Evaluate(p => values.GetValueOrDefault(p)));
    }

    [Fact]
    public void Evaluate_CoalesceFallsBackToNextPath()
    {
        var result = DisplayRuleParser.Parse("${name ?? globalId}");
        var values = new Dictionary<string, object?> { ["name"] = null, ["globalId"] = "space-eg-01" };

        Assert.Equal("space-eg-01", result.Evaluate(p => values.GetValueOrDefault(p)));
    }

    [Fact]
    public void Evaluate_EmptyStringTreatedAsUnset_FallsBack()
    {
        var result = DisplayRuleParser.Parse("${name ?? globalId}");
        var values = new Dictionary<string, object?> { ["name"] = "", ["globalId"] = "space-eg-01" };

        Assert.Equal("space-eg-01", result.Evaluate(p => values.GetValueOrDefault(p)));
    }

    [Fact]
    public void Evaluate_AllPlaceholdersEmpty_ReturnsNull()
    {
        var result = DisplayRuleParser.Parse("${roomNumber} - ${name}");

        Assert.Null(result.Evaluate(_ => null));
    }

    [Fact]
    public void Evaluate_PartiallyResolved_KeepsLiterals()
    {
        var result = DisplayRuleParser.Parse("${roomNumber} - ${name}");
        var values = new Dictionary<string, object?> { ["roomNumber"] = "EG01" };

        Assert.Equal("EG01 -", result.Evaluate(p => values.GetValueOrDefault(p)));
    }

    [Fact]
    public void Evaluate_FormatsNumbersInvariant()
    {
        var result = DisplayRuleParser.Parse("${temperature} C");
        var values = new Dictionary<string, object?> { ["temperature"] = 21.5 };

        Assert.Equal("21.5 C", result.Evaluate(p => values.GetValueOrDefault(p)));
    }
}
