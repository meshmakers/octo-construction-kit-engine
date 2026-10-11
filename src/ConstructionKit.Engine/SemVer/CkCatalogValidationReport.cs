using System.Text;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Renders a <see cref="CkCatalogValidationResult" /> as console text and Markdown (AB#4469).
/// </summary>
public static class CkCatalogValidationReport
{
    private static string Severity(CkCatalogValidationResult result) => result.DefectsAreErrors ? "ERROR" : "WARNING";

    /// <summary>The label of a model line.</summary>
    public static string Label(CkCatalogValidationResult result, CkCatalogModelCheck check) =>
        check.IsResolved ? "Resolved" : Severity(result);

    /// <summary>One-line summary.</summary>
    public static string Summary(CkCatalogValidationResult result) =>
        $"{result.Models.Count} model(s): {result.Models.Count - result.BrokenCount} resolved, {result.BrokenCount} " +
        "do not resolve" +
        (result.CaseVariants.Count > 0 ? $"; {result.CaseVariants.Count} model name(s) with case-only variants" : "") +
        (result.HasDefects
            ? result.DefectsAreErrors
                ? " - defects are ERRORS in this catalog (exit code non-zero)"
                : " - defects are only WARNINGS in this catalog (exit code 0)"
            : "");

    private static IEnumerable<string> HeaderLines(CkCatalogValidationResult result)
    {
        yield return $"Catalog: {result.CatalogName}";
        yield return result.DefectsAreErrors
            ? "Severity: Meshmakers-run catalog, defects are errors"
            : "Severity: defects are warnings (not a Meshmakers-run catalog)";
    }

    /// <summary>Console text: header, one block per model, case-only variants, summary.</summary>
    public static string RenderConsole(CkCatalogValidationResult result)
    {
        var text = new StringBuilder();
        foreach (var line in HeaderLines(result))
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        foreach (var check in result.Models)
        {
            text.AppendLine($"  {Label(result, check),-9} {check.Model.FullName} " +
                            $"({(check.IsRangeRetaining ? "range-retaining" : "exact pins")})");
            foreach (var finding in check.Findings)
            {
                text.AppendLine($"      {finding}");
            }

            foreach (var note in check.Notes.Where(n => n.StartsWith("not in range", StringComparison.Ordinal)))
            {
                text.AppendLine($"      {note}");
            }
        }

        foreach (var group in result.CaseVariants)
        {
            text.AppendLine();
            text.AppendLine($"  {Severity(result),-9} model name with case-only variants:");
            foreach (var variant in group.Variants)
            {
                text.AppendLine($"      {variant.Spelling} (newest {variant.NewestVersion}) at {variant.CatalogPath}");
            }
        }

        text.AppendLine();
        text.AppendLine(Summary(result));
        return text.ToString();
    }

    /// <summary>Markdown: header, table of every model, case-only variants.</summary>
    public static string RenderMarkdown(CkCatalogValidationResult result)
    {
        var text = new StringBuilder("# Construction Kit Catalog Validation\n\n");
        foreach (var line in HeaderLines(result))
        {
            text.Append($"- {line}\n");
        }

        text.Append($"\n{Summary(result)}\n\n");
        text.Append("| Model | Kind | Result | Detail |\n| ----- | ---- | ------ | ------ |\n");
        foreach (var check in result.Models)
        {
            var detail = check.Findings.Concat(check.Notes.Where(n => n.StartsWith("not in range", StringComparison.Ordinal)))
                .Select(d => d.Replace("|", "\\|"));
            text.Append($"| `{check.Model.FullName}` | {(check.IsRangeRetaining ? "range-retaining" : "exact pins")} | " +
                        $"**{Label(result, check)}** | {string.Join("<br>", detail)} |\n");
        }

        if (result.CaseVariants.Count > 0)
        {
            text.Append("\n### Model names that differ only in case\n\n| Spelling | Newest version | Catalog path |\n| -------- | -------------- | ------------ |\n");
            foreach (var variant in result.CaseVariants.SelectMany(g => g.Variants))
            {
                text.Append($"| `{variant.Spelling}` | {variant.NewestVersion} | `{variant.CatalogPath}` |\n");
            }
        }

        text.Append("\n_The check proves that dependencies resolve and that the referenced surface still binds at the latest " +
                    "versions. It cannot prove behavioural changes or runtime data._\n");
        return text.ToString();
    }
}
