using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

public class BlueprintIdVersionRangeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithCompleteString_ParsesCorrectly()
    {
        var range = new BlueprintIdVersionRange("InfrastructureStarter-1.0.0");
        Assert.Equal("InfrastructureStarter", range.Name);
        Assert.Equal("1.0.0", range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithVersionRange_ParsesCorrectly()
    {
        var range = new BlueprintIdVersionRange("MyBlueprint-[1.0.0,2.0.0)");
        Assert.Equal("MyBlueprint", range.Name);
        Assert.Equal("[1.0.0,2.0.0)", range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithoutVersion_DefaultsTo100()
    {
        var range = new BlueprintIdVersionRange("MyBlueprint");
        Assert.Equal("MyBlueprint", range.Name);
        Assert.Equal("1.0.0", range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithTwoParameters_SetsCorrectly()
    {
        var range = new BlueprintIdVersionRange("MyBlueprint", "[1.0.0,2.0.0)");
        Assert.Equal("MyBlueprint", range.Name);
        Assert.Equal("[1.0.0,2.0.0)", range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithEmptyName_CreatesEmptyInstance()
    {
        var range = new BlueprintIdVersionRange("", "1.0.0");
        Assert.Equal("", range.Name);
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void Constructor_WithNullName_CreatesEmptyInstance()
    {
        var range = new BlueprintIdVersionRange(null!, "1.0.0");
        Assert.Equal("", range.Name);
        Assert.True(range.IsEmpty);
    }

    #endregion

    #region Implicit Operator Tests

    [Fact]
    public void ImplicitOperator_FromString_CreatesInstance()
    {
        BlueprintIdVersionRange range = "ECommerce-1.0.0";
        Assert.Equal("ECommerce", range.Name);
        Assert.Equal("1.0.0", range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void ImplicitOperator_WithVersionRange_CreatesInstance()
    {
        BlueprintIdVersionRange range = "Infrastructure-[1.0,2.0)";
        Assert.Equal("Infrastructure", range.Name);
        Assert.Equal("[1.0,2.0)", range.BlueprintVersionRange.ToString());
    }

    #endregion

    #region Property Tests

    [Fact]
    public void FullName_WithNormalBlueprintId_ReturnsCorrectFormat()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.Equal("Blueprint-1.0.0", range.FullName);
    }

    [Fact]
    public void FullName_WithVersionRange_ReturnsCorrectFormat()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        Assert.Equal("Blueprint-[1.0.0,2.0.0)", range.FullName);
    }

    [Fact]
    public void FullName_WithEmptyName_ReturnsEmpty()
    {
        var range = new BlueprintIdVersionRange("", "1.0.0");
        Assert.Equal("", range.FullName);
    }

    [Fact]
    public void SemanticVersionedFullName_WithNormalBlueprintId_ReturnsCorrectFormat()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.Equal("Blueprint-1.0.0", range.SemanticVersionedFullName);
    }

    [Fact]
    public void IsEmpty_WithEmptyName_ReturnsTrue()
    {
        var range = new BlueprintIdVersionRange("", "1.0.0");
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithWhitespaceName_ReturnsTrue()
    {
        var range = new BlueprintIdVersionRange("  ", "1.0.0");
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithValidName_ReturnsFalse()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.False(range.IsEmpty);
    }

    #endregion

    #region IsSatisfiedBy Tests

    [Fact]
    public void IsSatisfiedBy_ExactVersion_ReturnsTrue()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var id = new BlueprintId("Blueprint", "1.0.0");
        Assert.True(range.IsSatisfiedBy(id));
    }

    [Fact]
    public void IsSatisfiedBy_DifferentBlueprintId_ReturnsFalse()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var id = new BlueprintId("Other", "1.0.0");
        Assert.False(range.IsSatisfiedBy(id));
    }

    [Fact]
    public void IsSatisfiedBy_VersionInRange_ReturnsTrue()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "1.5.0");
        var id3 = new BlueprintId("Blueprint", "1.9.9");

        Assert.True(range.IsSatisfiedBy(id1));
        Assert.True(range.IsSatisfiedBy(id2));
        Assert.True(range.IsSatisfiedBy(id3));
    }

    [Fact]
    public void IsSatisfiedBy_VersionOutOfRange_ReturnsFalse()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        var id1 = new BlueprintId("Blueprint", "0.9.9");
        var id2 = new BlueprintId("Blueprint", "2.0.0");
        var id3 = new BlueprintId("Blueprint", "2.0.1");

        Assert.False(range.IsSatisfiedBy(id1));
        Assert.False(range.IsSatisfiedBy(id2));
        Assert.False(range.IsSatisfiedBy(id3));
    }

    [Fact]
    public void IsSatisfiedBy_WithMinimumVersionRange_WorksCorrectly()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[2.0.0,)");

        Assert.False(range.IsSatisfiedBy(new BlueprintId("Blueprint", "1.9.9")));
        Assert.True(range.IsSatisfiedBy(new BlueprintId("Blueprint", "2.0.0")));
        Assert.True(range.IsSatisfiedBy(new BlueprintId("Blueprint", "3.0.0")));
        Assert.True(range.IsSatisfiedBy(new BlueprintId("Blueprint", "100.0.0")));
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_SameBlueprintAndVersion_ReturnsZero()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.Equal(0, range1.CompareTo(range2));
    }

    [Fact]
    public void CompareTo_DifferentBlueprintId_ReturnsCorrectOrder()
    {
        var range1 = new BlueprintIdVersionRange("Alpha", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Beta", "1.0.0");
        Assert.True(range1.CompareTo(range2) < 0);
        Assert.True(range2.CompareTo(range1) > 0);
    }

    [Fact]
    public void CompareTo_SameBlueprintDifferentVersion_ReturnsCorrectOrder()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Blueprint", "[2.0.0,)");
        Assert.True(range1.CompareTo(range2) < 0);
        Assert.True(range2.CompareTo(range1) > 0);
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.True(range.CompareTo(null) > 0);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_SameBlueprintAndOverlappingRange_ReturnsTrue()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        var range2 = new BlueprintIdVersionRange("Blueprint", "[1.5.0,2.5.0)");
        Assert.True(range1.Equals(range2));
    }

    [Fact]
    public void Equals_SameBlueprintNonOverlappingRange_ReturnsFalse()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        var range2 = new BlueprintIdVersionRange("Blueprint", "[2.0.0,3.0.0)");
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_DifferentBlueprint_ReturnsFalse()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint1", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Blueprint2", "1.0.0");
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.False(range.Equals(null));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFullName()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.Equal("Blueprint-1.0.0", range.ToString());
    }

    [Fact]
    public void ToString_WithVersionRange_ReturnsCorrectFormat()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[1.0.0,2.0.0)");
        Assert.Equal("Blueprint-[1.0.0,2.0.0)", range.ToString());
    }

    [Fact]
    public void ToString_WithEmptyName_ReturnsEmpty()
    {
        var range = new BlueprintIdVersionRange("", "1.0.0");
        Assert.Equal("", range.ToString());
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        Assert.Equal(range1.GetHashCode(), range2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_ReturnsDifferentHash()
    {
        var range1 = new BlueprintIdVersionRange("Blueprint", "1.0.0");
        var range2 = new BlueprintIdVersionRange("Blueprint", "2.0.0");
        var range3 = new BlueprintIdVersionRange("Other", "1.0.0");

        Assert.NotEqual(range1.GetHashCode(), range2.GetHashCode());
        Assert.NotEqual(range1.GetHashCode(), range3.GetHashCode());
    }

    #endregion

    #region Complex Scenarios

    [Theory]
    [InlineData("Blueprint-1.0.0", "Blueprint", "1.0.0")]
    [InlineData("Blueprint-[1.0,2.0)", "Blueprint", "[1.0,2.0)")]
    [InlineData("Blueprint-(1.0,)", "Blueprint", "(1.0,)")]
    [InlineData("Blueprint-(,2.0]", "Blueprint", "(,2.0]")]
    public void Constructor_VariousFormats_ParsesCorrectly(string input, string expectedName, string expectedRange)
    {
        var range = new BlueprintIdVersionRange(input);
        Assert.Equal(expectedName, range.Name);
        Assert.Equal(expectedRange, range.BlueprintVersionRange.ToString());
    }

    [Fact]
    public void IsSatisfiedBy_WithExactVersionRange_WorksCorrectly()
    {
        var range = new BlueprintIdVersionRange("Blueprint", "[1.5.0]");

        Assert.False(range.IsSatisfiedBy(new BlueprintId("Blueprint", "1.4.9")));
        Assert.True(range.IsSatisfiedBy(new BlueprintId("Blueprint", "1.5.0")));
        Assert.False(range.IsSatisfiedBy(new BlueprintId("Blueprint", "1.5.1")));
    }

    #endregion
}
