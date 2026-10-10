using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     AB#6271 rows D1–D7: dependency range and floor changes of range-retaining models.
/// </summary>
public class DependencyRangeRowTests
{
    private static CkCompiledModelRoot RangeRetaining(string range = "[2.4,3.0)", string floor = "2.4.0")
    {
        var model = SemVerTestModels.CreateModel();
        model.DependencyRanges = [new CkModelDependencyDto { Range = $"Base-{range}", Floor = floor }];
        return model;
    }

    [Fact]
    public void D1_FloorRaisedWithinTheMajor_IsOneMinorChange()
    {
        var change = Assert.Single(Classify(RangeRetaining(), RangeRetaining(floor: "2.5.0")));

        Assert.Equal(CkModelElementKind.DependencyRange, change.Change.ElementKind);
        Assert.Equal("floor", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    [Fact]
    public void D2_FloorLoweredOrRangeWidened_IsMinor()
    {
        Assert.Equal(CkSemVerLevel.Minor, Level(RangeRetaining(floor: "2.5.0"), RangeRetaining()));
        Assert.Equal(CkSemVerLevel.Minor, Level(RangeRetaining(), RangeRetaining("[2.2,3.0)", "2.2.0")));
    }

    [Fact]
    public void D3_UpperBoundNarrowedWithinTheMajor_IsMinor()
    {
        var change = Assert.Single(Classify(RangeRetaining(), RangeRetaining("[2.4,2.9)")));

        Assert.Equal("range", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }

    private static CkCompiledModelRoot ResolvedAt(string version, string range = "[2.4,3.0)", string floor = "2.4.0")
    {
        var model = RangeRetaining(range, floor);
        model.Dependencies = [new CkModelId($"Base-{version}")];
        return model;
    }

    [Fact]
    public void D3_RangeExcludingThePreviousResolution_IsMajor()
    {
        // AB#6340 (gate case 5g): the previous release resolved Base 2.6.0; [2.4,2.5) excludes it, so a tenant that
        // installed the previous release with 2.6.0 cannot import this minor (it would need a downgrade).
        var classified = Classify(ResolvedAt("2.6.0"), ResolvedAt("2.4.0", "[2.4,2.5)"));
        Assert.Contains(classified, c => c.Change.Property == "range" && c.Level == CkSemVerLevel.Major &&
                                         c.Reason.Contains("(2.6.0)"));
        Assert.Equal(CkSemVerLevel.Major, classified.Max(c => c.Level));

        // A raised floor above the previous resolution is the same case.
        Assert.Equal(CkSemVerLevel.Major, Level(ResolvedAt("2.6.0"), ResolvedAt("2.7.0", floor: "2.7.0")));
    }

    [Fact]
    public void D3_NarrowingThatStillIncludesThePreviousResolution_IsMinorWithANote()
    {
        var classified = Classify(ResolvedAt("2.6.0"), ResolvedAt("2.6.0", "[2.4,2.7)"));
        var change = Assert.Single(classified);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.True(change.IsBehavioural);
        Assert.Contains("cannot import", change.Reason);
    }

    [Fact]
    public void D3_ResolvedDependencyVersionWentDown_IsMajor()
    {
        // Range unchanged, but the closure resolved a lower version than the previous release.
        var change = Assert.Single(Classify(ResolvedAt("2.6.0"), ResolvedAt("2.5.0")));
        Assert.Equal(CkModelElementKind.Dependency, change.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Major, change.Level);

        // Exact pins (no range retention) keep the v1 rule: Minor.
        var v1Baseline = SemVerTestModels.CreateModel();
        v1Baseline.Dependencies = [new CkModelId("Base-2.6.0")];
        var v1Current = SemVerTestModels.CreateModel();
        v1Current.Dependencies = [new CkModelId("Base-2.5.0")];
        Assert.Equal(CkSemVerLevel.Minor, Level(v1Baseline, v1Current));
    }

    [Fact]
    public void D1_FloorRaised_WithinThePreviousResolution_IsMinor_BeyondIt_IsMajor()
    {
        // N5 (platform-owner decision 2026-10-10): raising the floor up to the version the previous release resolved
        // is Minor; beyond it the previous resolution is excluded (Major, with a reason that does not claim a
        // downgrade).
        Assert.Equal(CkSemVerLevel.Minor, Level(ResolvedAt("2.6.0"), ResolvedAt("2.6.0", floor: "2.6.0")));
        var beyond = Assert.Single(Classify(ResolvedAt("2.6.0"), ResolvedAt("2.6.0", floor: "2.7.0")),
            c => c.Change.Property == "floor");
        Assert.Equal(CkSemVerLevel.Major, beyond.Level);
        Assert.DoesNotContain("downgrade", beyond.Reason);
        Assert.Contains("(2.6.0)", beyond.Reason);
    }

    [Fact]
    public void D6_SwitchToExactPinsExcludingThePreviousResolution_IsMajor()
    {
        // N3 (platform-owner decision 2026-10-10): the previous release was range-retaining and resolved Base 2.6.0;
        // the new release pins exactly another version.
        var exact = SemVerTestModels.CreateModel();
        exact.Dependencies = [new CkModelId("Base-2.5.0")];
        var change = Assert.Single(Classify(ResolvedAt("2.6.0"), exact), c => c.Change.ElementKind == CkModelElementKind.Dependency);
        Assert.Equal(CkSemVerLevel.Major, change.Level);

        // The same pin as the previous resolution stays the D6 Minor.
        var same = SemVerTestModels.CreateModel();
        same.Dependencies = [new CkModelId("Base-2.6.0")];
        Assert.Equal(CkSemVerLevel.Minor, Level(ResolvedAt("2.6.0"), same));
    }

    [Fact]
    public void D4_RangeMovedToAnotherMajor_IsMajor()
    {
        var classified = Classify(RangeRetaining(), RangeRetaining("[3.0,4.0)", "3.0.0"));

        Assert.Equal(2, classified.Count);
        Assert.All(classified, c => Assert.Equal(CkSemVerLevel.Major, c.Level));
    }

    [Fact]
    public void D5_RangeDependencyAdded_IsMinor_Removed_IsMajor()
    {
        var two = RangeRetaining();
        two.DependencyRanges!.Add(new CkModelDependencyDto { Range = "Other-[1.0,2.0)", Floor = "1.0.0" });

        Assert.Equal(CkSemVerLevel.Minor, Level(RangeRetaining(), two));
        Assert.Equal(CkSemVerLevel.Major, Level(two, RangeRetaining()));
    }

    [Fact]
    public void D6_SwitchBetweenExactPinsAndRangeRetention_IsMinor_WithReferenceToTheRePin()
    {
        var change = Assert.Single(Classify(SemVerTestModels.CreateModel(), RangeRetaining()));
        Assert.Equal("rangeRetention", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
        Assert.Contains("F2.6", change.Reason);

        Assert.Equal(CkSemVerLevel.Minor, Assert.Single(Classify(RangeRetaining(), SemVerTestModels.CreateModel())).Level);
    }

    /// <summary>
    ///     Row D7: <c>usedSurface</c> is derived from the model's own references, which are classified elsewhere, so it is
    ///     never classified on its own. Until it exists (AB#4472) there is nothing to diff; once it does, it must be a
    ///     documented exclusion of <see cref="CkModelDiffService.ExcludedProperties" />.
    /// </summary>
    [Fact]
    public void D7_UsedSurface_IsNotClassifiedOnItsOwn()
    {
        foreach (var property in new[] { nameof(CkModelDependencyDto.UsedSurface), nameof(CkModelDependencyDto.UsedSurfaceHash) })
        {
            Assert.DoesNotContain(property, CkModelDiffService.ComparedProperties[typeof(CkModelDependencyDto)]);
            Assert.True(CkModelDiffService.ExcludedProperties[typeof(CkModelDependencyDto)].ContainsKey(property),
                $"{property} must be a documented exclusion (row D7), not a compared property.");
        }

        // A changed usedSurface alone produces no change.
        var baseline = RangeRetaining();
        baseline.DependencyRanges![0].UsedSurface = ["Base@2/Entity-1"];
        var current = RangeRetaining();
        current.DependencyRanges![0].UsedSurface = ["Base@2/Entity-1", "Base@2/Name-1"];
        Assert.Empty(Classify(baseline, current));
    }

    [Fact]
    public void ExactPinnedModels_KeepTheirClassification()
    {
        var current = SemVerTestModels.CreateModel();
        current.Dependencies = [new Contracts.CkModelId("Base", "1.3.0")];

        var change = Assert.Single(Classify(SemVerTestModels.CreateModel(), current));
        Assert.Equal(CkModelElementKind.Dependency, change.Change.ElementKind);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }
}
