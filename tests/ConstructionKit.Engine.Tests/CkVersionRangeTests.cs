using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests;

public class CkVersionRangeTests
{
    [Fact]
    public void SimpleVersion_Should_CreateMinimumInclusiveRange()
    {
        var range = new CkVersionRange("1.0.0");
        var version = new CkVersion("1.0.0");

        Assert.True(range.IsSatisfiedBy(version));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.1.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("0.9.0")));
    }

    [Fact]
    public void ExactVersionMatch_Should_OnlyMatchExactVersion()
    {
        var range = new CkVersionRange("[1.0.0]");

        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.1")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("0.9.0")));
    }

    [Fact]
    public void InclusiveRange_Should_IncludeBounds()
    {
        var range = new CkVersionRange("[1.0.0,2.0.0]");

        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.5.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("2.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("0.9.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("2.0.1")));
    }

    [Fact]
    public void ExclusiveRange_Should_ExcludeBounds()
    {
        var range = new CkVersionRange("(1.0.0,2.0.0)");

        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.5.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("2.0.0")));
    }

    [Fact]
    public void MixedRange_Should_RespectInclusiveAndExclusiveBounds()
    {
        var range = new CkVersionRange("[1.0.0,2.0.0)");

        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.9.9")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("2.0.0")));
    }

    [Fact]
    public void MaximumVersionInclusive_Should_Work()
    {
        var range = new CkVersionRange("(,1.0.0]");

        Assert.True(range.IsSatisfiedBy(new CkVersion("0.9.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.1")));
    }

    [Fact]
    public void MaximumVersionExclusive_Should_Work()
    {
        var range = new CkVersionRange("(,1.0.0)");

        Assert.True(range.IsSatisfiedBy(new CkVersion("0.9.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.1")));
    }

    [Fact]
    public void MinimumVersionExclusive_Should_Work()
    {
        var range = new CkVersionRange("(1.0.0,)");

        Assert.False(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.1")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("2.0.0")));
    }

    [Fact]
    public void ShortVersionFormat_Should_Work()
    {
        var range = new CkVersionRange("[1.0,2.0]");

        Assert.True(range.IsSatisfiedBy(new CkVersion("1.0.0")));
        Assert.True(range.IsSatisfiedBy(new CkVersion("2.0.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("0.9.0")));
        Assert.False(range.IsSatisfiedBy(new CkVersion("2.0.1")));
    }

    [Fact]
    public void InvalidFormat_SingleVersionInParentheses_Should_ThrowException()
    {
        Assert.Throws<ArgumentException>(() => new CkVersionRange("(1.0.0)"));
    }

    [Fact]
    public void InvalidFormat_NoCommaInRange_Should_ThrowException()
    {
        Assert.Throws<ArgumentException>(() => new CkVersionRange("(1.0.0 2.0.0)"));
    }

    [Fact]
    public void ToString_Should_ReturnOriginalRange()
    {
        var originalRange = "[1.0.0,2.0.0)";
        var range = new CkVersionRange(originalRange);

        Assert.Equal(originalRange, range.ToString());
    }


    [Fact]
    public void Overlaps_Should_DifferentSingleVersionsDoNotOverlap()
    {
        var range1 = new CkVersionRange("[1.0.0]");
        var range2 = new CkVersionRange("[1.0.1]");

        Assert.False(range1.Overlaps(range2));
        Assert.False(range2.Overlaps(range1));
    }

    [Fact]
    public void Overlaps_Should_DetectOverlappingRanges()
    {
        var range1 = new CkVersionRange("[1.0.0,2.0.0]");
        var range2 = new CkVersionRange("[1.5.0,3.0.0]");
        var range3 = new CkVersionRange("[3.0.0,4.0.0]");

        Assert.True(range1.Overlaps(range2));
        Assert.True(range2.Overlaps(range1));
        Assert.False(range1.Overlaps(range3));
    }

    [Fact]
    public void CompareTo_Should_OrderByMinimumVersionFirst()
    {
        var range1 = new CkVersionRange("[1.0.0,2.0.0]");
        var range2 = new CkVersionRange("[1.5.0,3.0.0]");
        var range3 = new CkVersionRange("[0.5.0,1.0.0]");

        Assert.True(range1.CompareTo(range2) < 0); // 1.0.0 < 1.5.0
        Assert.True(range3.CompareTo(range1) < 0); // 0.5.0 < 1.0.0
        Assert.True(range2.CompareTo(range1) > 0); // 1.5.0 > 1.0.0
    }

    [Fact]
    public void CompareTo_Should_CompareInclusivityWhenMinVersionsEqual()
    {
        var inclusive = new CkVersionRange("[1.0.0,2.0.0]");
        var exclusive = new CkVersionRange("(1.0.0,2.0.0]");

        Assert.True(inclusive.CompareTo(exclusive) < 0); // inclusive < exclusive
        Assert.True(exclusive.CompareTo(inclusive) > 0); // exclusive > inclusive
    }

    [Fact]
    public void CompareTo_Should_CompareMaxVersionWhenMinVersionsEqual()
    {
        var range1 = new CkVersionRange("[1.0.0,2.0.0]");
        var range2 = new CkVersionRange("[1.0.0,3.0.0]");

        Assert.True(range1.CompareTo(range2) < 0); // 2.0.0 < 3.0.0
        Assert.True(range2.CompareTo(range1) > 0); // 3.0.0 > 2.0.0
    }

    [Fact]
    public void CompareTo_Should_HandleUnboundedRanges()
    {
        var unboundedMin = new CkVersionRange("(,2.0.0]");
        var boundedMin = new CkVersionRange("[1.0.0,2.0.0]");
        var unboundedMax = new CkVersionRange("[1.0.0,)");
        var boundedMax = new CkVersionRange("[1.0.0,2.0.0]");

        Assert.True(unboundedMin.CompareTo(boundedMin) < 0); // no min < min
        Assert.True(boundedMax.CompareTo(unboundedMax) < 0); // max < no max
    }

    [Fact]
    public void CompareTo_Should_ReturnZeroForEqualRanges()
    {
        var range1 = new CkVersionRange("[1.0.0,2.0.0]");
        var range2 = new CkVersionRange("[1.0.0,2.0.0]");

        Assert.Equal(0, range1.CompareTo(range2));
        Assert.Equal(0, range2.CompareTo(range1));
    }

    [Fact]
    public void CompareTo_Should_CompareMaxInclusivityWhenAllElseEqual()
    {
        var inclusive = new CkVersionRange("[1.0.0,2.0.0]");
        var exclusive = new CkVersionRange("[1.0.0,2.0.0)");

        Assert.True(exclusive.CompareTo(inclusive) < 0); // exclusive max < inclusive max
        Assert.True(inclusive.CompareTo(exclusive) > 0); // inclusive max > exclusive max
    }
}