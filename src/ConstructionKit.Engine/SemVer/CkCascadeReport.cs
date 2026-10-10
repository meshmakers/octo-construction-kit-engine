using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Renders a <see cref="CkCascadeResult" /> as console text and Markdown (AB#5437).
/// </summary>
public static class CkCascadeReport
{
    /// <summary>The display label of a verdict.</summary>
    public static string Label(CkDependentVerdict verdict) => verdict switch
    {
        CkDependentVerdict.Compatible => "Compatible",
        CkDependentVerdict.NeedsRepin => "NeedsRepin",
        CkDependentVerdict.Breaks => "Breaks",
        _ => "NotInRange"
    };

    /// <summary>One-line summary, e.g. <c>2 dependents: 1 Breaks, 0 NeedsRepin, 1 Compatible, 0 NotInRange</c>.</summary>
    public static string Summary(CkCascadeResult result)
    {
        int Count(CkDependentVerdict v) => result.Dependents.Count(d => d.Verdict == v);
        return $"{result.Dependents.Count} dependent(s): {Count(CkDependentVerdict.Breaks)} Breaks, " +
               $"{Count(CkDependentVerdict.NeedsRepin)} NeedsRepin, {Count(CkDependentVerdict.Compatible)} Compatible, " +
               $"{Count(CkDependentVerdict.NotInRange)} NotInRange";
    }

    /// <summary>The header lines: candidate, baseline and the candidate's own verdict.</summary>
    public static IReadOnlyList<string> Header(CkCascadeResult result)
    {
        var lines = new List<string> { $"Candidate: {result.Candidate.ModelId.FullName}" };
        var baseline = result.Baseline.Baseline;
        if (baseline == null)
        {
            lines.Add(result.Baseline.SourceUnreachable
                ? "Baseline: unknown (a catalog source was unreachable) — incompatible changes and name collisions cannot be detected, only missing elements"
                : "Baseline: none (first publication) — incompatible changes and name collisions cannot be detected, only missing elements");
        }
        else
        {
            lines.Add($"Baseline: {baseline.FullName} ({result.Baseline.CatalogName}{(result.Baseline.IsLocal ? ", local, not published" : "")})");
            if (result.CandidateVerdict != null)
            {
                var v = result.CandidateVerdict;
                lines.Add(v.RequiredLevel == CkSemVerLevel.None
                    ? "Candidate change level: none (no structural changes)"
                    : $"Candidate change level: {CkModelChangeFormatter.GetLevelLabel(v.RequiredLevel)} (minimum version {v.Validation.MinimumVersion}, declared {v.Validation.DeclaredVersion}: {(v.Validation.IsValid ? "ok" : "too low")})");
            }
        }

        return lines;
    }

    /// <summary>Console text: header, one block per dependent, summary.</summary>
    public static string RenderConsole(CkCascadeResult result)
    {
        var text = new StringBuilder();
        foreach (var line in Header(result))
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        foreach (var dependent in result.Dependents)
        {
            text.AppendLine($"  {Label(dependent.Verdict),-11} {dependent.Dependent.FullName} " +
                            $"({(dependent.IsRangeRetaining ? "range-retaining" : "exact pins")}" +
                            $"{(dependent.RequiredLevel is { } level ? $", {CkModelChangeFormatter.GetLevelLabel(level)} bump" : "")})");
            foreach (var reason in dependent.Reasons)
            {
                text.AppendLine($"      {reason}");
            }
        }

        foreach (var warning in result.LoadWarnings)
        {
            text.AppendLine($"  Warning: {warning}");
        }

        text.AppendLine();
        text.AppendLine(Summary(result));
        return text.ToString();
    }

    /// <summary>Markdown: header, table of every dependent with verdict and reasons, warnings.</summary>
    public static string RenderMarkdown(CkCascadeResult result)
    {
        var text = new StringBuilder("# Construction Kit Cascade Dry Run\n\n");
        foreach (var line in Header(result))
        {
            text.Append($"- {line}\n");
        }

        text.Append($"\n{Summary(result)}\n\n");
        text.Append("| Dependent | Kind | Verdict | Reason |\n| --------- | ---- | ------- | ------ |\n");
        foreach (var dependent in result.Dependents)
        {
            var reasons = string.Join("<br>", dependent.Reasons.Select(r => r.Replace("|", "\\|")));
            var verdict = dependent.RequiredLevel is { } level
                ? $"{Label(dependent.Verdict)} ({CkModelChangeFormatter.GetLevelLabel(level)})"
                : Label(dependent.Verdict);
            text.Append($"| `{dependent.Dependent.FullName}` | {(dependent.IsRangeRetaining ? "range-retaining" : "exact pins")} | **{verdict}** | {reasons} |\n");
        }

        if (result.LoadWarnings.Count > 0)
        {
            text.Append("\n### Warnings\n\n");
            foreach (var warning in result.LoadWarnings)
            {
                text.Append($"- {warning}\n");
            }
        }

        text.Append("\n_The dry run proves that the dependents' referenced surface still binds. It cannot prove behavioural " +
                    "changes (defaults, display rules, indexes) or runtime data._\n");
        return text.ToString();
    }
}
