using System.Globalization;
using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Default implementation of <see cref="ICkChangelogGenerator" />.
/// </summary>
public class CkChangelogGenerator : ICkChangelogGenerator
{
    private const string DefaultHeader = "# Changelog\n\n";

    /// <inheritdoc />
    public string Generate(string? existingContent, CkVersion version, DateTime date, CkSemVerLevel requiredLevel,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, string? note = null)
    {
        var (header, sections) = ParseSections(existingContent);
        var versionToken = version.ToString();

        var replaceIndex = sections.FindIndex(s => s.VersionToken == versionToken);
        var hasFollowingSection = replaceIndex >= 0 ? replaceIndex < sections.Count - 1 : sections.Count > 0;
        var newSection = BuildSection(version, date, requiredLevel, classifiedChanges, note, hasFollowingSection);

        var result = new StringBuilder(header);
        if (replaceIndex >= 0)
        {
            // Idempotent update: only the section of the given version is replaced,
            // all other sections stay byte-identical.
            for (var i = 0; i < sections.Count; i++)
            {
                result.Append(i == replaceIndex ? newSection : sections[i].Text);
            }
        }
        else
        {
            // Insert the new section on top — newest first
            result.Append(newSection);
            foreach (var section in sections)
            {
                result.Append(section.Text);
            }
        }

        return result.ToString();
    }

    private static (string Header, List<(string VersionToken, string Text)> Sections) ParseSections(
        string? existingContent)
    {
        if (string.IsNullOrWhiteSpace(existingContent) || existingContent == null)
        {
            return (DefaultHeader, []);
        }

        var sectionStarts = new List<int>();
        var offset = 0;
        while (offset <= existingContent.Length - 3)
        {
            var index = existingContent.IndexOf("## ", offset, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            // Only accept headings at the start of a line, and not "### " sub-headings
            var isLineStart = index == 0 || existingContent[index - 1] == '\n';
            if (isLineStart)
            {
                sectionStarts.Add(index);
            }

            offset = index + 3;
        }

        if (sectionStarts.Count == 0)
        {
            return (existingContent, []);
        }

        var header = existingContent.Substring(0, sectionStarts[0]);
        var sections = new List<(string VersionToken, string Text)>();
        for (var i = 0; i < sectionStarts.Count; i++)
        {
            var start = sectionStarts[i];
            var end = i < sectionStarts.Count - 1 ? sectionStarts[i + 1] : existingContent.Length;
            var text = existingContent.Substring(start, end - start);
            sections.Add((GetVersionToken(text), text));
        }

        return (header, sections);
    }

    private static string GetVersionToken(string sectionText)
    {
        // Section text starts with "## <version> ..." — the token ends at the first
        // whitespace or line break after the heading marker
        var token = sectionText.Substring(3);
        var endIndex = token.IndexOfAny([' ', '\t', '\r', '\n']);
        return endIndex < 0 ? token : token.Substring(0, endIndex);
    }

    private static string BuildSection(CkVersion version, DateTime date, CkSemVerLevel requiredLevel,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, string? note, bool hasFollowingSection)
    {
        var section = new StringBuilder();
        section.Append(
            $"## {version} - {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\n\n");

        if (note != null)
        {
            section.Append($"_{note}_\n\n");
        }

        if (classifiedChanges.Count > 0)
        {
            section.Append(
                $"_Required bump level: {CkModelChangeFormatter.GetLevelLabel(requiredLevel).ToLowerInvariant()}._\n\n");

            AppendGroup(section, "Breaking", classifiedChanges
                .Where(c => c.Level == CkSemVerLevel.Major));
            AppendGroup(section, "Added", classifiedChanges
                .Where(c => c.Level != CkSemVerLevel.Major && c.Change.ChangeKind == CkModelChangeKind.Added));
            AppendGroup(section, "Changed", classifiedChanges
                .Where(c => c.Level != CkSemVerLevel.Major && c.Change.ChangeKind != CkModelChangeKind.Added));
        }

        // Trim the trailing blank line of the last group, then terminate the section
        while (section.Length >= 1 && section[section.Length - 1] == '\n')
        {
            section.Length -= 1;
        }

        section.Append('\n');
        if (hasFollowingSection)
        {
            section.Append('\n');
        }

        return section.ToString();
    }

    private static void AppendGroup(StringBuilder section, string heading,
        IEnumerable<CkClassifiedModelChange> changes)
    {
        var changeList = changes.ToList();
        if (changeList.Count == 0)
        {
            return;
        }

        section.Append($"### {heading}\n\n");
        foreach (var change in changeList)
        {
            section.Append(
                $"- {CkModelChangeFormatter.Format(change.Change)} _({CkModelChangeFormatter.GetLevelLabel(change.Level).ToLowerInvariant()} — {change.Reason})_\n");
        }

        section.Append('\n');
    }
}
