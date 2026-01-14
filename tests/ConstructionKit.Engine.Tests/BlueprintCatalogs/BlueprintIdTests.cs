using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

public class BlueprintIdTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithCompleteString_ParsesCorrectly()
    {
        var id = new BlueprintId("InfrastructureStarter-1.0.0");
        Assert.Equal("InfrastructureStarter", id.Name);
        Assert.Equal("1.0.0", id.Version.ToString());
    }

    [Fact]
    public void Constructor_WithTwoParameters_SetsCorrectly()
    {
        var id = new BlueprintId("MyBlueprint", "2.3.4");
        Assert.Equal("MyBlueprint", id.Name);
        Assert.Equal("2.3.4", id.Version.ToString());
    }

    [Fact]
    public void Constructor_WithoutVersion_DefaultsTo100()
    {
        var id = new BlueprintId("MyBlueprint");
        Assert.Equal("MyBlueprint", id.Name);
        Assert.Equal("1.0.0", id.Version.ToString());
    }

    [Fact]
    public void Constructor_WithEmptyName_CreatesEmptyInstance()
    {
        var id = new BlueprintId("", "1.0.0");
        Assert.Equal("", id.Name);
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void Constructor_WithNullName_CreatesEmptyInstance()
    {
        var id = new BlueprintId(null!, "1.0.0");
        Assert.Equal("", id.Name);
        Assert.True(id.IsEmpty);
    }

    #endregion

    #region Implicit Operator Tests

    [Fact]
    public void ImplicitOperator_FromString_CreatesInstance()
    {
        BlueprintId id = "ECommerce-2.0.0";
        Assert.Equal("ECommerce", id.Name);
        Assert.Equal("2.0.0", id.Version.ToString());
    }

    [Fact]
    public void ImplicitOperator_WithoutVersion_UsesDefault()
    {
        BlueprintId id = "SimpleBlueprint";
        Assert.Equal("SimpleBlueprint", id.Name);
        Assert.Equal("1.0.0", id.Version.ToString());
    }

    #endregion

    #region Property Tests

    [Fact]
    public void FullName_ReturnsCorrectFormat()
    {
        var id = new BlueprintId("InfrastructureStarter", "1.0.0");
        Assert.Equal("InfrastructureStarter-1.0.0", id.FullName);
    }

    [Fact]
    public void FullName_WithEmptyName_ReturnsEmpty()
    {
        var id = new BlueprintId("", "1.0.0");
        Assert.Equal("", id.FullName);
    }

    [Fact]
    public void SemanticVersionedFullName_WithMajorVersion1_ReturnsNameOnly()
    {
        // SemanticVersionedFullName only adds major version if > 1
        var id = new BlueprintId("TestBlueprint", "1.2.3");
        Assert.Equal("TestBlueprint", id.SemanticVersionedFullName);
    }

    [Fact]
    public void SemanticVersionedFullName_WithMajorVersionGreaterThan1_ReturnsNameWithMajor()
    {
        var id = new BlueprintId("TestBlueprint", "2.0.0");
        Assert.Equal("TestBlueprint-2", id.SemanticVersionedFullName);
    }

    [Fact]
    public void IsEmpty_WithEmptyName_ReturnsTrue()
    {
        var id = new BlueprintId("", "1.0.0");
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithWhitespaceName_ReturnsTrue()
    {
        var id = new BlueprintId("  ", "1.0.0");
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithValidName_ReturnsFalse()
    {
        var id = new BlueprintId("ValidBlueprint", "1.0.0");
        Assert.False(id.IsEmpty);
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_SameNameAndVersion_ReturnsZero()
    {
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "1.0.0");
        Assert.Equal(0, id1.CompareTo(id2));
    }

    [Fact]
    public void CompareTo_DifferentName_ReturnsCorrectOrder()
    {
        var id1 = new BlueprintId("Alpha", "1.0.0");
        var id2 = new BlueprintId("Beta", "1.0.0");
        Assert.True(id1.CompareTo(id2) < 0);
        Assert.True(id2.CompareTo(id1) > 0);
    }

    [Fact]
    public void CompareTo_SameNameDifferentVersion_ReturnsCorrectOrder()
    {
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "2.0.0");
        Assert.True(id1.CompareTo(id2) < 0);
        Assert.True(id2.CompareTo(id1) > 0);
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive()
    {
        var id = new BlueprintId("Blueprint", "1.0.0");
        Assert.True(id.CompareTo(null) > 0);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_SameNameAndVersion_ReturnsTrue()
    {
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "1.0.0");
        Assert.True(id1.Equals(id2));
    }

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
    {
        var id1 = new BlueprintId("Blueprint1", "1.0.0");
        var id2 = new BlueprintId("Blueprint2", "1.0.0");
        Assert.False(id1.Equals(id2));
    }

    [Fact]
    public void Equals_DifferentVersion_ReturnsFalse()
    {
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "2.0.0");
        Assert.False(id1.Equals(id2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var id = new BlueprintId("Blueprint", "1.0.0");
        Assert.False(id.Equals(null));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFullName()
    {
        var id = new BlueprintId("Blueprint", "1.2.3");
        Assert.Equal("Blueprint-1.2.3", id.ToString());
    }

    [Fact]
    public void ToString_WithEmptyName_ReturnsEmpty()
    {
        var id = new BlueprintId("", "1.0.0");
        Assert.Equal("", id.ToString());
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var id1 = new BlueprintId("Blueprint", "1.0.0");
        var id2 = new BlueprintId("Blueprint", "1.0.0");
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    #endregion

    #region Version Parsing Tests

    [Theory]
    [InlineData("Blueprint-1.0.0", "Blueprint", "1.0.0")]
    [InlineData("Blueprint-2.3.4", "Blueprint", "2.3.4")]
    [InlineData("Simple", "Simple", "1.0.0")]
    public void Constructor_VariousFormats_ParsesCorrectly(string input, string expectedName, string expectedVersion)
    {
        var id = new BlueprintId(input);
        Assert.Equal(expectedName, id.Name);
        Assert.Equal(expectedVersion, id.Version.ToString());
    }

    #endregion

    #region ToVersionRange Tests

    [Fact]
    public void ToVersionRange_ReturnsExactVersionRange()
    {
        var id = new BlueprintId("Blueprint", "1.2.3");
        var range = id.ToVersionRange();

        Assert.Equal("Blueprint", range.Name);
        Assert.True(range.IsSatisfiedBy(id));
    }

    #endregion
}
