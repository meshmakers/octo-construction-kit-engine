using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The release configuration of a number range (<c>System/AutoIncrement.Release</c>) and the one rule of
///     design note D8 - a document is released when its state value is one of <c>ReleasedValues</c>.
/// </summary>
public class NumberRangeReleaseTests
{
    private const string Model = "ReleaseTest-1.0.0";
    private static readonly CkId<CkEnumId> StateEnumId = new($"{Model}/DocumentStates");

    private static readonly CkTypeAttributeGraph EnumState = Attribute("DocumentState", AttributeValueTypesDto.Enum,
        StateEnumId);

    private static readonly CkTypeAttributeGraph StringState = Attribute("Status", AttributeValueTypesDto.String);

    private static readonly CkEnumGraph StateEnum = new(StateEnumId, new CkEnumDto
    {
        EnumId = "DocumentStates",
        Values =
        [
            new CkEnumValueDto { Key = 0, Name = "Draft" },
            new CkEnumValueDto { Key = 1, Name = "Released" },
            new CkEnumValueDto { Key = 2, Name = "Cancelled" }
        ]
    });

    [Fact]
    public void RangeWithoutRelease_HasNoConfiguration()
    {
        Assert.True(NumberRangeRelease.TryCreate(new RtEntity(), out var release, out var error));
        Assert.Null(release);
        Assert.Null(error);
    }

    [Fact]
    public void ValidRelease_IsRead()
    {
        var counter = Counter("DocumentState", ["Released", "Cancelled"], ["PaymentState"]);

        Assert.True(NumberRangeRelease.TryCreate(counter, out var release, out var error), error);
        Assert.NotNull(release);
        Assert.Equal("DocumentState", release.StateAttribute);
        Assert.Equal(["Cancelled", "Released"], release.ReleasedValues.Order(StringComparer.Ordinal));
        Assert.Equal(["PaymentState"], release.ChangeableAttributes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void ReleaseWithoutStateAttribute_IsRefused(string? stateAttribute)
    {
        var counter = Counter(stateAttribute, ["Released"]);

        Assert.False(NumberRangeRelease.TryCreate(counter, out var release, out var error));
        Assert.Null(release);
        Assert.Contains("StateAttribute", error);
    }

    [Fact]
    public void ReleaseWithoutReleasedValues_IsRefused()
    {
        Assert.False(NumberRangeRelease.TryCreate(Counter("DocumentState", []), out _, out var error));
        Assert.Contains("ReleasedValues", error);
        Assert.False(NumberRangeRelease.TryCreate(Counter("DocumentState", null), out _, out error));
        Assert.Contains("ReleasedValues", error);
    }

    [Fact]
    public void EnumState_IsComparedByTheNameOfItsValue()
    {
        var release = Release("DocumentState", ["Released", "Cancelled"]);

        Assert.False(release.IsReleased(0, EnumState, StateEnum));
        Assert.True(release.IsReleased(1, EnumState, StateEnum));
        Assert.True(release.IsReleased(2L, EnumState, StateEnum));
        Assert.True(release.IsReleased("Released", EnumState, StateEnum));
        Assert.False(release.IsReleased(7, EnumState, StateEnum));
        Assert.False(release.IsReleased(null, EnumState, StateEnum));
    }

    [Fact]
    public void StringState_IsComparedCaseSensitively()
    {
        var release = Release("Status", ["RELEASED"]);

        Assert.True(release.IsReleased("RELEASED", StringState, null));
        Assert.False(release.IsReleased("Released", StringState, null));
        Assert.False(release.IsReleased("DRAFT", StringState, null));
        Assert.False(release.IsReleased(null, StringState, null));
    }

    private static NumberRangeRelease Release(string stateAttribute, List<string> releasedValues)
    {
        Assert.True(NumberRangeRelease.TryCreate(Counter(stateAttribute, releasedValues), out var release,
            out var error), error);
        return release!;
    }

    private static RtEntity Counter(string? stateAttribute, List<string>? releasedValues,
        List<string>? changeableAttributes = null)
    {
        var record = new RtRecord();
        record.SetAttributeRawValue(NumberRangeRelease.StateAttributeAttribute, stateAttribute);
        record.SetAttributeRawValue(NumberRangeRelease.ReleasedValuesAttribute, releasedValues);
        if (changeableAttributes != null)
        {
            record.SetAttributeRawValue(NumberRangeRelease.ChangeableAttributesAttribute, changeableAttributes);
        }

        var counter = new RtEntity();
        counter.SetAttributeRawValue(NumberRangeRelease.ReleaseAttribute, record);
        return counter;
    }

    private static CkTypeAttributeGraph Attribute(string name, AttributeValueTypesDto valueType,
        CkId<CkEnumId>? enumId = null)
    {
        var attributeId = new CkId<CkAttributeId>($"{Model}/{name}");
        var attributeGraph = new CkAttributeGraph(attributeId,
            new CkAttributeDto { AttributeId = name, ValueType = valueType, ValueCkEnumId = enumId });
        return new CkTypeAttributeGraph(attributeId,
            new CkTypeAttributeDto { CkAttributeId = attributeId, AttributeName = name, IsOptional = true },
            attributeGraph);
    }
}
