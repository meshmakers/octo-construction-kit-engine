using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     Covers all cases of the version validation rule (FR-7) and the CkVersion bump helpers.
/// </summary>
public class CkSemVerValidationRuleTests
{
    private readonly CkSemVerClassifier _classifier = new();

    [Fact]
    public void EmptyDiff_SameVersion_IsValid()
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion("2.0.2"),
            CkSemVerLevel.None);

        Assert.Equal(CkSemVerVerdict.Valid, result.Verdict);
        Assert.True(result.IsValid);
        Assert.Equal(new CkVersion("2.0.2"), result.MinimumVersion);
    }

    [Fact]
    public void EmptyDiff_HigherVersion_IsValidWithNote()
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion("2.1.0"),
            CkSemVerLevel.None);

        Assert.Equal(CkSemVerVerdict.ValidBumpWithoutStructuralChange, result.Verdict);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(CkSemVerLevel.Patch, "2.0.3")]
    [InlineData(CkSemVerLevel.Minor, "2.1.0")]
    [InlineData(CkSemVerLevel.Major, "3.0.0")]
    public void Diff_ExactMinimumBump_IsValid(CkSemVerLevel requiredLevel, string declared)
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion(declared),
            requiredLevel);

        Assert.Equal(CkSemVerVerdict.Valid, result.Verdict);
        Assert.Equal(new CkVersion(declared), result.MinimumVersion);
    }

    [Theory]
    [InlineData(CkSemVerLevel.Patch, "2.1.0")]
    [InlineData(CkSemVerLevel.Minor, "3.0.0")]
    [InlineData(CkSemVerLevel.Minor, "2.2.5")]
    public void Diff_HigherThanRequiredBump_IsValid(CkSemVerLevel requiredLevel, string declared)
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion(declared),
            requiredLevel);

        Assert.Equal(CkSemVerVerdict.Valid, result.Verdict);
    }

    [Theory]
    [InlineData(CkSemVerLevel.Patch, "2.0.2")]
    [InlineData(CkSemVerLevel.Minor, "2.0.3")]
    [InlineData(CkSemVerLevel.Major, "2.1.0")]
    public void Diff_ForgottenOrTooLowBump_IsVersionTooLow(CkSemVerLevel requiredLevel, string declared)
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion(declared),
            requiredLevel);

        Assert.Equal(CkSemVerVerdict.VersionTooLow, result.Verdict);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(CkSemVerLevel.None)]
    [InlineData(CkSemVerLevel.Minor)]
    public void DeclaredBelowPublished_IsDowngrade(CkSemVerLevel requiredLevel)
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.0.2"), new CkVersion("2.0.1"),
            requiredLevel);

        Assert.Equal(CkSemVerVerdict.Downgrade, result.Verdict);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void MinimumVersion_IsPublishedPlusExactlyOneBump()
    {
        var result = _classifier.ValidateDeclaredVersion(new CkVersion("2.3.4"), new CkVersion("4.0.0"),
            CkSemVerLevel.Minor);

        Assert.Equal(new CkVersion("2.4.0"), result.MinimumVersion);
        Assert.Equal(CkSemVerVerdict.Valid, result.Verdict);
    }

    // ── CkVersion helpers ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CkSemVerLevel.None, "2.3.4")]
    [InlineData(CkSemVerLevel.Patch, "2.3.5")]
    [InlineData(CkSemVerLevel.Minor, "2.4.0")]
    [InlineData(CkSemVerLevel.Major, "3.0.0")]
    public void Bump_ReturnsExactlyOneStep(CkSemVerLevel level, string expected)
    {
        Assert.Equal(new CkVersion(expected), new CkVersion("2.3.4").Bump(level));
    }

    [Theory]
    [InlineData("2.4.0", CkSemVerLevel.Minor, true)]
    [InlineData("3.0.0", CkSemVerLevel.Minor, true)]
    [InlineData("2.3.5", CkSemVerLevel.Minor, false)]
    [InlineData("2.3.5", CkSemVerLevel.Patch, true)]
    public void IsAtLeastBumpOf_ComparesAgainstMinimum(string candidate, CkSemVerLevel level, bool expected)
    {
        Assert.Equal(expected, new CkVersion(candidate).IsAtLeastBumpOf(new CkVersion("2.3.4"), level));
    }

    [Fact]
    public void ComponentConstructor_RejectsNegativeParts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkVersion(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkVersion(1, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CkVersion(1, 0, -1));
    }
}
