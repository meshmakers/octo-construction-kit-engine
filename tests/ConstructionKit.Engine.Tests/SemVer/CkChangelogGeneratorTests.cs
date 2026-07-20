using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

public class CkChangelogGeneratorTests
{
    private static readonly DateTime Date = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly CkChangelogGenerator _generator = new();

    private static CkClassifiedModelChange Change(CkSemVerLevel level, CkModelChangeKind changeKind,
        CkModelElementKind elementKind, string elementId, string reason, string? property = null)
    {
        return new CkClassifiedModelChange
        {
            Change = new CkModelChange
            {
                ChangeKind = changeKind, ElementKind = elementKind, ElementId = elementId, Property = property,
                OldValue = changeKind == CkModelChangeKind.Modified ? "a" : null,
                NewValue = changeKind == CkModelChangeKind.Modified ? "b" : null
            },
            Level = level,
            Reason = reason
        };
    }

    private static readonly IReadOnlyList<CkClassifiedModelChange> SampleChanges =
    [
        Change(CkSemVerLevel.Major, CkModelChangeKind.Removed, CkModelElementKind.Attribute, "Old-1",
            "consumers reference the removed element"),
        Change(CkSemVerLevel.Minor, CkModelChangeKind.Added, CkModelElementKind.EnumValue, "State-1/Standby",
            "purely additive enum value"),
        Change(CkSemVerLevel.Patch, CkModelChangeKind.Modified, CkModelElementKind.Type, "Machine-1",
            "purely documentational change", "description")
    ];

    [Fact]
    public void Generate_WithoutExistingChangelog_CreatesHeaderAndSection()
    {
        var content = _generator.Generate(null, new CkVersion("2.0.0"), Date, CkSemVerLevel.Major, SampleChanges);

        Assert.StartsWith("# Changelog\n\n## 2.0.0 - 2026-07-17\n", content);
        Assert.Contains("_Required bump level: major._", content);
        Assert.Contains("### Breaking", content);
        Assert.Contains("### Added", content);
        Assert.Contains("### Changed", content);
        Assert.Contains("Attribute 'Old-1' removed", content);
        Assert.Contains("Enum value 'State-1/Standby' added", content);
        Assert.Contains("Machine-1", content);
    }

    [Fact]
    public void Generate_GroupsChangesByBreakingAddedChanged()
    {
        var content = _generator.Generate(null, new CkVersion("2.0.0"), Date, CkSemVerLevel.Major, SampleChanges);

        var breakingIndex = content.IndexOf("### Breaking", StringComparison.Ordinal);
        var addedIndex = content.IndexOf("### Added", StringComparison.Ordinal);
        var changedIndex = content.IndexOf("### Changed", StringComparison.Ordinal);
        Assert.True(breakingIndex < addedIndex);
        Assert.True(addedIndex < changedIndex);

        var breakingSection = content.Substring(breakingIndex, addedIndex - breakingIndex);
        Assert.Contains("Old-1", breakingSection);
        Assert.DoesNotContain("Standby", breakingSection);
    }

    [Fact]
    public void Generate_KeepsOlderSectionsByteIdentical()
    {
        var existing = "# Changelog\n\n## 1.1.0 - 2026-01-05\n\nhand-written   entry  \twith  odd spacing\n\n" +
                       "## 1.0.0 - 2025-11-01\n\n- initial\n";

        var content = _generator.Generate(existing, new CkVersion("2.0.0"), Date, CkSemVerLevel.Major, SampleChanges);

        Assert.StartsWith("# Changelog\n\n## 2.0.0 - 2026-07-17\n", content);
        Assert.Contains("## 1.1.0 - 2026-01-05\n\nhand-written   entry  \twith  odd spacing\n\n", content);
        Assert.EndsWith("## 1.0.0 - 2025-11-01\n\n- initial\n", content);
    }

    [Fact]
    public void Generate_IsIdempotentForRepeatedRuns()
    {
        var first = _generator.Generate(null, new CkVersion("2.0.0"), Date, CkSemVerLevel.Major, SampleChanges);
        var second = _generator.Generate(first, new CkVersion("2.0.0"), Date, CkSemVerLevel.Major, SampleChanges);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_ReplacesOnlyTheSectionOfTheGivenVersion()
    {
        var older = "## 1.0.0 - 2025-11-01\n\n- initial\n";
        var existing = "# Changelog\n\n## 1.1.0 - 2026-01-05\n\n- old content of 1.1.0\n\n" + older;

        var content = _generator.Generate(existing, new CkVersion("1.1.0"), Date, CkSemVerLevel.Minor,
        [
            Change(CkSemVerLevel.Minor, CkModelChangeKind.Added, CkModelElementKind.Enum, "NewEnum-1",
                "purely additive element")
        ]);

        Assert.DoesNotContain("old content of 1.1.0", content);
        Assert.Contains("## 1.1.0 - 2026-07-17", content);
        Assert.Contains("NewEnum-1", content);
        Assert.EndsWith(older, content);
        Assert.StartsWith("# Changelog\n\n## 1.1.0", content);
    }

    [Fact]
    public void Generate_RepeatedSectionReplacement_IsIdempotent()
    {
        var existing = "# Changelog\n\n## 1.0.0 - 2025-11-01\n\n- initial\n";
        var changes = new[]
        {
            Change(CkSemVerLevel.Minor, CkModelChangeKind.Added, CkModelElementKind.Enum, "NewEnum-1",
                "purely additive element")
        };

        var first = _generator.Generate(existing, new CkVersion("1.1.0"), Date, CkSemVerLevel.Minor, changes);
        var second = _generator.Generate(first, new CkVersion("1.1.0"), Date, CkSemVerLevel.Minor, changes);

        Assert.Equal(first, second);
        Assert.EndsWith("## 1.0.0 - 2025-11-01\n\n- initial\n", second);
    }

    [Fact]
    public void Generate_WithNote_RendersNoteBelowHeading()
    {
        var content = _generator.Generate(null, new CkVersion("1.0.0"), Date, CkSemVerLevel.None, [],
            "Initial publication.");

        Assert.Equal("# Changelog\n\n## 1.0.0 - 2026-07-17\n\n_Initial publication._\n", content);
    }

    [Fact]
    public void Generate_DoesNotConfuseSubHeadingsWithVersionSections()
    {
        var existing = "# Changelog\n\n## 1.0.0 - 2025-11-01\n\n### Added\n\n- something\n";

        var content = _generator.Generate(existing, new CkVersion("1.1.0"), Date, CkSemVerLevel.Minor,
        [
            Change(CkSemVerLevel.Minor, CkModelChangeKind.Added, CkModelElementKind.Enum, "NewEnum-1",
                "purely additive element")
        ]);

        Assert.EndsWith("## 1.0.0 - 2025-11-01\n\n### Added\n\n- something\n", content);
        Assert.Contains("## 1.1.0 - 2026-07-17", content);
    }
}
