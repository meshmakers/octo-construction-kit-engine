using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkModelIdVersionRangeTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithCompleteString_ParsesCorrectly()
    {
        var range = new CkModelIdVersionRange("System-1.0.0");
        Assert.Equal("System", range.ModelId);
        Assert.Equal("1.0.0", range.ModelVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithVersionRange_ParsesCorrectly()
    {
        var range = new CkModelIdVersionRange("System-[1.0.0,2.0.0)");
        Assert.Equal("System", range.ModelId);
        Assert.Equal("[1.0.0,2.0.0)", range.ModelVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithoutVersion_DefaultsTo100()
    {
        var range = new CkModelIdVersionRange("System");
        Assert.Equal("System", range.ModelId);
        Assert.Equal("1.0.0", range.ModelVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithTwoParameters_SetsCorrectly()
    {
        var range = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        Assert.Equal("System", range.ModelId);
        Assert.Equal("[1.0.0,2.0.0)", range.ModelVersionRange.ToString());
    }

    [Fact]
    public void Constructor_WithEmptyModelId_CreatesEmptyInstance()
    {
        var range = new CkModelIdVersionRange("", "1.0.0");
        Assert.Equal("", range.ModelId);
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void Constructor_WithNullModelId_CreatesEmptyInstance()
    {
        var range = new CkModelIdVersionRange(null!, "1.0.0");
        Assert.Equal("", range.ModelId);
        Assert.True(range.IsEmpty);
    }

    #endregion

    #region Implicit Operator Tests

    [Fact]
    public void ImplicitOperator_FromString_CreatesInstance()
    {
        CkModelIdVersionRange range = "System-1.0.0";
        Assert.Equal("System", range.ModelId);
        Assert.Equal("1.0.0", range.ModelVersionRange.ToString());
    }

    [Fact]
    public void ImplicitOperator_WithVersionRange_CreatesInstance()
    {
        CkModelIdVersionRange range = "System-[1.0,2.0)";
        Assert.Equal("System", range.ModelId);
        Assert.Equal("[1.0,2.0)", range.ModelVersionRange.ToString());
    }

    #endregion

    #region Property Tests

    [Fact]
    public void FullName_WithNormalModelId_ReturnsCorrectFormat()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal("System-1.0.0", range.FullName);
    }

    [Fact]
    public void FullName_WithSpecialModelId_ReturnsModelIdOnly()
    {
        var range = new CkModelIdVersionRange("$special", "1.0.0");
        Assert.Equal("$special", range.FullName);
    }

    [Fact]
    public void FullName_WithEmptyModelId_ReturnsEmpty()
    {
        var range = new CkModelIdVersionRange("", "1.0.0");
        Assert.Equal("", range.FullName);
    }

    [Fact]
    public void SemanticVersionedFullName_WithNormalModelId_ReturnsCorrectFormat()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal("System-1.0.0", range.SemanticVersionedFullName);
    }

    [Fact]
    public void SemanticVersionedFullName_WithEmptyModelId_ReturnsEmpty()
    {
        var range = new CkModelIdVersionRange("", "1.0.0");
        Assert.Equal("", range.SemanticVersionedFullName);
    }

    [Fact]
    public void IsEmpty_WithEmptyModelId_ReturnsTrue()
    {
        var range = new CkModelIdVersionRange("", "1.0.0");
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithWhitespaceModelId_ReturnsTrue()
    {
        var range = new CkModelIdVersionRange("  ", "1.0.0");
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithValidModelId_ReturnsFalse()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.False(range.IsEmpty);
    }

    #endregion

    #region IsSatisfiedBy Tests

    [Fact]
    public void IsSatisfiedBy_ExactVersion_ReturnsTrue()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        var modelId = new CkModelId("System", "1.0.0");
        Assert.True(range.IsSatisfiedBy(modelId));
    }

    [Fact]
    public void IsSatisfiedBy_DifferentModelId_ReturnsFalse()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        var modelId = new CkModelId("Other", "1.0.0");
        Assert.False(range.IsSatisfiedBy(modelId));
    }

    [Fact]
    public void IsSatisfiedBy_VersionInRange_ReturnsTrue()
    {
        var range = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var modelId1 = new CkModelId("System", "1.0.0");
        var modelId2 = new CkModelId("System", "1.5.0");
        var modelId3 = new CkModelId("System", "1.9.9");

        Assert.True(range.IsSatisfiedBy(modelId1));
        Assert.True(range.IsSatisfiedBy(modelId2));
        Assert.True(range.IsSatisfiedBy(modelId3));
    }

    [Fact]
    public void IsSatisfiedBy_VersionOutOfRange_ReturnsFalse()
    {
        var range = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var modelId1 = new CkModelId("System", "0.9.9");
        var modelId2 = new CkModelId("System", "2.0.0");
        var modelId3 = new CkModelId("System", "2.0.1");

        Assert.False(range.IsSatisfiedBy(modelId1));
        Assert.False(range.IsSatisfiedBy(modelId2));
        Assert.False(range.IsSatisfiedBy(modelId3));
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_SameModelAndVersion_ReturnsZero()
    {
        var range1 = new CkModelIdVersionRange("System", "1.0.0");
        var range2 = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal(0, range1.CompareTo(range2));
    }

    [Fact]
    public void CompareTo_DifferentModelId_ReturnsCorrectOrder()
    {
        var range1 = new CkModelIdVersionRange("Alpha", "1.0.0");
        var range2 = new CkModelIdVersionRange("Beta", "1.0.0");
        Assert.True(range1.CompareTo(range2) < 0);
        Assert.True(range2.CompareTo(range1) > 0);
    }

    [Fact]
    public void CompareTo_SameModelDifferentVersion_ReturnsCorrectOrder()
    {
        var range1 = new CkModelIdVersionRange("System", "1.0.0");
        var range2 = new CkModelIdVersionRange("System", "[2.0.0,)");
        Assert.True(range1.CompareTo(range2) < 0);
        Assert.True(range2.CompareTo(range1) > 0);
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.True(range.CompareTo(null) > 0);
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_SimpleVersions_OverlapDueToMinimumSemantics()
    {
        // IMPORTANT: Simple version "1.0.0" means >= 1.0.0, not exactly 1.0.0
        // Therefore these ranges DO overlap in the CkModelIdVersionRange.Equals implementation
        var range1 = new CkModelIdVersionRange("System", "1.0.0"); // [1.0.0,)
        var range2 = new CkModelIdVersionRange("System", "1.0.1"); // [1.0.1,)

        // The Equals method checks if version ranges overlap, and these do overlap
        // because [1.0.0,) includes [1.0.1,)
        Assert.True(range1.Equals(range2));
    }

    [Fact]
    public void Equals_SameModelAndOverlappingRange_ReturnsTrue()
    {
        var range1 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var range2 = new CkModelIdVersionRange("System", "[1.5.0,2.5.0)");
        Assert.True(range1.Equals(range2));
    }

    [Fact]
    public void Equals_SameModelNonOverlappingRange_ReturnsFalse()
    {
        var range1 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var range2 = new CkModelIdVersionRange("System", "[2.0.0,3.0.0)");
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_DifferentModel_ReturnsFalse()
    {
        var range1 = new CkModelIdVersionRange("System", "1.0.0");
        var range2 = new CkModelIdVersionRange("Other", "1.0.0");
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.False(range.Equals(null));
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ReturnsFullName()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal("System-1.0.0", range.ToString());
    }

    [Fact]
    public void ToString_WithVersionRange_ReturnsCorrectFormat()
    {
        var range = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        Assert.Equal("System-[1.0.0,2.0.0)", range.ToString());
    }

    [Fact]
    public void ToString_WithEmptyModelId_ReturnsEmpty()
    {
        var range = new CkModelIdVersionRange("", "1.0.0");
        Assert.Equal("", range.ToString());
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var range1 = new CkModelIdVersionRange("System", "1.0.0");
        var range2 = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal(range1.GetHashCode(), range2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_ReturnsDifferentHash()
    {
        var range1 = new CkModelIdVersionRange("System", "1.0.0");
        var range2 = new CkModelIdVersionRange("System", "2.0.0");
        var range3 = new CkModelIdVersionRange("Other", "1.0.0");

        Assert.NotEqual(range1.GetHashCode(), range2.GetHashCode());
        Assert.NotEqual(range1.GetHashCode(), range3.GetHashCode());
    }

    #endregion

    #region IConvertible Tests

    [Fact]
    public void GetTypeCode_ReturnsObject()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal(TypeCode.Object, range.GetTypeCode());
    }

    [Fact]
    public void ToBoolean_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToBoolean(null));
    }

    [Fact]
    public void ToByte_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToByte(null));
    }

    [Fact]
    public void ToChar_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToChar(null));
    }

    [Fact]
    public void ToDateTime_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToDateTime(null));
    }

    [Fact]
    public void ToDecimal_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToDecimal(null));
    }

    [Fact]
    public void ToDouble_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToDouble(null));
    }

    [Fact]
    public void ToInt16_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToInt16(null));
    }

    [Fact]
    public void ToInt32_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToInt32(null));
    }

    [Fact]
    public void ToInt64_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToInt64(null));
    }

    [Fact]
    public void ToSByte_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToSByte(null));
    }

    [Fact]
    public void ToSingle_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToSingle(null));
    }

    [Fact]
    public void ToString_WithProvider_ReturnsFullName()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Equal("System-1.0.0", range.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToType_String_ReturnsStringRepresentation()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        var result = range.ToType(typeof(string), null);
        Assert.Equal("System-1.0.0", result);
    }

    [Fact]
    public void ToType_Object_ReturnsSelf()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        var result = range.ToType(typeof(object), null);
        Assert.Same(range, result);
    }

    [Fact]
    public void ToType_CkModelId_ReturnsSelf()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        var result = range.ToType(typeof(CkModelId), null);
        Assert.Same(range, result);
    }

    [Fact]
    public void ToType_InvalidType_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToType(typeof(int), null));
        Assert.Throws<InvalidCastException>(() => range.ToType(typeof(DateTime), null));
    }

    [Fact]
    public void ToUInt16_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToUInt16(null));
    }

    [Fact]
    public void ToUInt32_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToUInt32(null));
    }

    [Fact]
    public void ToUInt64_ThrowsInvalidCastException()
    {
        var range = new CkModelIdVersionRange("System", "1.0.0");
        Assert.Throws<InvalidCastException>(() => range.ToUInt64(null));
    }

    #endregion

    #region Version Range Overlap Tests

    [Fact]
    public void Equals_ExactVersions_DoNotOverlap()
    {
        // Using exact version syntax [x.x.x] for non-overlapping versions
        var range1 = new CkModelIdVersionRange("System", "[1.0.0]");
        var range2 = new CkModelIdVersionRange("System", "[1.0.1]");

        // Exact versions [1.0.0] and [1.0.1] do NOT overlap
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_ExactVersionWithMinimumVersion_OverlapBehavior()
    {
        // Exact version [1.0.0] vs minimum version 1.0.0 (which means [1.0.0,))
        var exactRange = new CkModelIdVersionRange("System", "[1.0.0]");
        var minRange = new CkModelIdVersionRange("System", "1.0.0");

        // These overlap at exactly 1.0.0
        Assert.True(exactRange.Equals(minRange));
        Assert.True(minRange.Equals(exactRange));
    }

    [Fact]
    public void Equals_MinimumVersionRanges_AlwaysOverlap()
    {
        // All minimum-only version ranges overlap with each other
        var range1 = new CkModelIdVersionRange("System", "1.0.0"); // [1.0.0,)
        var range2 = new CkModelIdVersionRange("System", "2.0.0"); // [2.0.0,)
        var range3 = new CkModelIdVersionRange("System", "0.5.0"); // [0.5.0,)

        // All overlap because each extends to infinity
        Assert.True(range1.Equals(range2));
        Assert.True(range2.Equals(range3));
        Assert.True(range1.Equals(range3));
    }

    [Fact]
    public void Equals_NonOverlappingBoundedRanges_ReturnsFalse()
    {
        // Ranges that truly don't overlap
        var range1 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var range2 = new CkModelIdVersionRange("System", "[2.0.0,3.0.0)");

        // [1.0.0,2.0.0) and [2.0.0,3.0.0) don't overlap (exclusive upper, inclusive lower)
        Assert.False(range1.Equals(range2));
    }

    [Fact]
    public void Equals_OverlappingBoundedRanges_ReturnsTrue()
    {
        // Ranges with clear overlap
        var range1 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0]");
        var range2 = new CkModelIdVersionRange("System", "[2.0.0,3.0.0]");

        // These overlap at exactly 2.0.0 (both inclusive)
        Assert.True(range1.Equals(range2));
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void Constructor_WithComplexVersionRange_ParsesCorrectly()
    {
        var testCases = new[]
        {
            ("System-(1.0,)", "System", "(1.0,)"),
            ("System-(,2.0]", "System", "(,2.0]"),
            ("System-[1.0]", "System", "[1.0]"),
            ("MyModel-[1.2.3,4.5.6]", "MyModel", "[1.2.3,4.5.6]")
        };

        foreach (var (input, expectedId, expectedRange) in testCases)
        {
            var range = new CkModelIdVersionRange(input);
            Assert.Equal(expectedId, range.ModelId);
            Assert.Equal(expectedRange, range.ModelVersionRange.ToString());
        }
    }

    [Fact]
    public void IsSatisfiedBy_WithMinimumVersionRange_WorksCorrectly()
    {
        var range = new CkModelIdVersionRange("System", "[2.0.0,)");

        Assert.False(range.IsSatisfiedBy(new CkModelId("System", "1.9.9")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "2.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "3.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "100.0.0")));
    }

    [Fact]
    public void IsSatisfiedBy_WithMaximumVersionRange_WorksCorrectly()
    {
        var range = new CkModelIdVersionRange("System", "(,2.0.0]");

        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "0.0.1")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "1.9.9")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "2.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkModelId("System", "2.0.1")));
    }

    [Fact]
    public void IsSatisfiedBy_WithExactVersionRange_WorksCorrectly()
    {
        var range = new CkModelIdVersionRange("System", "[1.5.0]");

        Assert.False(range.IsSatisfiedBy(new CkModelId("System", "1.4.9")));
        Assert.True(range.IsSatisfiedBy(new CkModelId("System", "1.5.0")));
        Assert.False(range.IsSatisfiedBy(new CkModelId("System", "1.5.1")));
    }

    [Fact]
    public void Record_Equality_WorksCorrectly()
    {
        var range1 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var range2 = new CkModelIdVersionRange("System", "[1.0.0,2.0.0)");
        var range3 = new CkModelIdVersionRange("System", "[1.5.0,2.5.0)");

        Assert.True(range1.Equals(range2));
        Assert.True(range2.Equals(range1));
        Assert.True(range1.Equals(range3));
        Assert.True(range3.Equals(range1));
    }

    #endregion
}