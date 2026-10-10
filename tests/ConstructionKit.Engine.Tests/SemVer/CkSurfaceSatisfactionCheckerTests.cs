using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#5437: edge cases of the surface satisfaction checker that need no catalog. The end-to-end verdicts on
///     compiled models live in <c>CkCascadeTests</c> (Compiler.Tests).
/// </summary>
public class CkSurfaceSatisfactionCheckerTests
{
    private static CkSurfaceCandidate Candidate(string version = "2.3.0") =>
        CkSurfaceCandidate.Create(
            new CkCompiledModelRoot { ModelId = new CkModelId("Base", version), Types = [], Attributes = [] }, null, []);

    private static CkCompiledModelRoot Dependent(params string[] dependencies) => new()
    {
        ModelId = new CkModelId("Dep", "1.0.0"), Dependencies = dependencies.Select(d => new CkModelId(d)).ToList()
    };

    private static readonly CkSurfaceSatisfactionChecker Checker = new(new CkSemVerClassifier());

    [Fact]
    public void A_model_that_names_the_candidate_nowhere_is_compatible_and_says_it_is_only_indirectly_affected()
    {
        var check = Checker.Check(Candidate(), Dependent("Other-1.0.0"));

        Assert.Equal(CkDependentVerdict.Compatible, check.Verdict);
        Assert.Contains("no direct dependency on Base", Assert.Single(check.Reasons));
    }

    [Fact]
    public void An_exact_pin_of_another_major_is_not_in_range()
    {
        var check = Checker.Check(Candidate("3.0.0"), Dependent("Base-2.5.0"));

        Assert.Equal(CkDependentVerdict.NotInRange, check.Verdict);
    }

    [Fact]
    public void An_exact_pin_of_the_candidate_version_itself_is_compatible_without_a_repin()
    {
        var check = Checker.Check(Candidate("2.5.0"), Dependent("Base-2.5.0"));

        Assert.Equal(CkDependentVerdict.Compatible, check.Verdict);
        Assert.Null(check.RequiredLevel);
    }

    [Fact]
    public void An_exact_pin_of_an_older_minor_needs_a_minor_repin()
    {
        var check = Checker.Check(Candidate("2.6.0"), Dependent("Base-2.5.0"));

        Assert.Equal(CkDependentVerdict.NeedsRepin, check.Verdict);
        Assert.Equal(Contracts.SemVer.CkSemVerLevel.Minor, check.RequiredLevel);
    }

    [Fact]
    public void A_used_member_the_candidate_no_longer_declares_breaks_the_dependent()
    {
        var candidate = CkSurfaceCandidate.Create(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Base", "2.3.0"),
            Types = [new CkCompiledTypeDto { TypeId = "Entity", Attributes = [] }]
        }, null, []);
        var dependent = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Dep", "1.0.0"),
            DependencyRanges =
            [
                new CkModelDependencyDto
                {
                    Range = new CkModelIdVersionRange("Base", "[2.0,3.0)"), Floor = "2.0.0",
                    UsedSurface = ["Base@2/Entity-1.Name"]
                }
            ]
        };

        var check = Checker.Check(candidate, dependent);

        Assert.Equal(CkDependentVerdict.Breaks, check.Verdict);
        Assert.Contains("'Name' is no longer declared", Assert.Single(check.Reasons));
    }

    [Fact]
    public void A_used_member_the_candidate_still_declares_keeps_the_dependent_compatible()
    {
        var candidate = CkSurfaceCandidate.Create(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Base", "2.3.0"),
            Types =
            [
                new CkCompiledTypeDto
                {
                    TypeId = "Entity",
                    Attributes = [new CkTypeAttributeDto { CkAttributeId = "Base/Name", AttributeName = "Name" }]
                }
            ]
        }, null, []);
        var dependent = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Dep", "1.0.0"),
            DependencyRanges =
            [
                new CkModelDependencyDto
                {
                    Range = new CkModelIdVersionRange("Base", "[2.0,3.0)"), Floor = "2.0.0",
                    UsedSurface = ["Base@2/Entity-1.name"]
                }
            ]
        };

        Assert.Equal(CkDependentVerdict.Compatible, Checker.Check(candidate, dependent).Verdict);
    }

    [Fact]
    public void A_dependency_name_that_differs_only_in_case_is_still_checked()
    {
        // The compiler accepts "system-[2.2,3.0)" where the file system ignores case (macOS, Windows), so the
        // dependent can carry a differently cased name. The check must not go blind for it. (Platform independent:
        // the dependent is built by hand, not compiled.)
        var candidate = CkSurfaceCandidate.Create(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("System", "2.3.0"), Types = [], Attributes = []
        }, null, []);
        var dependent = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("PlantL", "1.0.0"),
            DependencyRanges =
            [
                new CkModelDependencyDto { Range = new CkModelIdVersionRange("system", "[2.2,3.0)"), Floor = "2.2.0" }
            ]
        };

        var check = Checker.Check(candidate, dependent);

        Assert.Equal(CkDependentVerdict.Compatible, check.Verdict);
        Assert.Contains("range [2.2,3.0) admits 2.3.0", Assert.Single(check.Reasons));
    }
}
