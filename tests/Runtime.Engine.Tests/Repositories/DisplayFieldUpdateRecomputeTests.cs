using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     Smart display-field recompute on partial updates (AB#4811): touch detection by root
///     attribute (records included), merged resolution (updated attributes win over stored ones)
///     and the empty-string clear sentinel.
/// </summary>
public class DisplayFieldUpdateRecomputeTests
{
    private static RtEntity CreateEntity(Dictionary<string, object?> attributes)
    {
        return new RtEntity(new RtCkId<CkTypeId>("Test/Space"), OctoObjectId.GenerateNewId(), attributes);
    }

    [Fact]
    public void GetValidParseResult_NullOrInvalidRule_ReturnsNull()
    {
        Assert.Null(DisplayFieldUpdateRecompute.GetValidParseResult(null));
        Assert.Null(DisplayFieldUpdateRecompute.GetValidParseResult("  "));
        Assert.Null(DisplayFieldUpdateRecompute.GetValidParseResult("${unterminated"));
    }

    [Fact]
    public void GetReferencedRootAttributes_UnionOfBothRules_RecordPathsReducedToRoot()
    {
        var nameParse = DisplayFieldUpdateRecompute.GetValidParseResult("${RoomNumber} - ${Name}");
        var descriptionParse = DisplayFieldUpdateRecompute.GetValidParseResult("${Thermal.SpaceTemperature}");

        var roots = DisplayFieldUpdateRecompute.GetReferencedRootAttributes(nameParse, descriptionParse);

        Assert.Contains("RoomNumber", roots);
        Assert.Contains("Name", roots);
        Assert.Contains("Thermal", roots);
        Assert.Equal(3, roots.Count);
    }

    [Fact]
    public void TouchesReferencedAttributes_MatchesRootAttribute()
    {
        var roots = new HashSet<string> { "Name", "Thermal" };

        Assert.True(DisplayFieldUpdateRecompute.TouchesReferencedAttributes(
            CreateEntity(new() { ["Name"] = "x" }), roots));
        Assert.True(DisplayFieldUpdateRecompute.TouchesReferencedAttributes(
            CreateEntity(new() { ["Thermal"] = new RtRecord() }), roots));
        Assert.False(DisplayFieldUpdateRecompute.TouchesReferencedAttributes(
            CreateEntity(new() { ["Unrelated"] = 42 }), roots));
    }

    [Fact]
    public void Recompute_UpdatedAttributeWins_StoredFillsTheRest()
    {
        var nameParse = DisplayFieldUpdateRecompute.GetValidParseResult("${RoomNumber} - ${Name}");
        var partial = CreateEntity(new() { ["Name"] = "Neuer Name" });
        var stored = CreateEntity(new() { ["RoomNumber"] = "EG01", ["Name"] = "Alter Name" });

        DisplayFieldUpdateRecompute.Recompute(nameParse, null, partial, stored);

        Assert.Equal("EG01 - Neuer Name", partial.RtDisplayName);
        Assert.Null(partial.RtDisplayDescription);
    }

    [Fact]
    public void Recompute_UpdatedRecordWins()
    {
        var nameParse = DisplayFieldUpdateRecompute.GetValidParseResult("${Thermal.SpaceTemperature} C");
        var partial = CreateEntity(new()
        {
            ["Thermal"] = new RtRecord(new RtCkId<CkRecordId>("Test/Req"),
                new Dictionary<string, object?> { ["SpaceTemperature"] = 22 })
        });
        var stored = CreateEntity(new()
        {
            ["Thermal"] = new RtRecord(new RtCkId<CkRecordId>("Test/Req"),
                new Dictionary<string, object?> { ["SpaceTemperature"] = 21 })
        });

        DisplayFieldUpdateRecompute.Recompute(nameParse, null, partial, stored);

        Assert.Equal("22 C", partial.RtDisplayName);
    }

    [Fact]
    public void Recompute_UpdateClearsValue_WritesEmptyStringSentinel()
    {
        var nameParse = DisplayFieldUpdateRecompute.GetValidParseResult("${Name}");
        var partial = CreateEntity(new() { ["Name"] = null });
        var stored = CreateEntity(new() { ["Name"] = "Alter Name" });

        DisplayFieldUpdateRecompute.Recompute(nameParse, null, partial, stored);

        Assert.Equal(string.Empty, partial.RtDisplayName);
    }

    [Fact]
    public void Recompute_BothRules_IndependentResolution()
    {
        var nameParse = DisplayFieldUpdateRecompute.GetValidParseResult("${Name}");
        var descriptionParse = DisplayFieldUpdateRecompute.GetValidParseResult("${Description ?? Name}");
        var partial = CreateEntity(new() { ["Name"] = "Neuer Name" });
        var stored = CreateEntity(new() { ["Name"] = "Alter Name", ["Description"] = null });

        DisplayFieldUpdateRecompute.Recompute(nameParse, descriptionParse, partial, stored);

        Assert.Equal("Neuer Name", partial.RtDisplayName);
        Assert.Equal("Neuer Name", partial.RtDisplayDescription);
    }
}
