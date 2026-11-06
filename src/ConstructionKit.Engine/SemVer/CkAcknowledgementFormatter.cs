using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The texts of the acknowledge findings (AB#6295), shared by <c>ValidateVersion</c> and the compile gate so both
///     print the same <c>OCTO-CK203</c> / <c>OCTO-CK204</c> messages.
/// </summary>
public static class CkAcknowledgementFormatter
{
    /// <summary>The code of a change that needs an acknowledgement and has none.</summary>
    public const string MissingCode = "OCTO-CK203";

    /// <summary>The code of an acknowledgement that matches no change of the release.</summary>
    public const string StaleCode = "OCTO-CK204";

    /// <summary>
    ///     The code of an acknowledgement entry of a compiled model that violates the structural rules (empty reason,
    ///     wildcard, duplicate key, not plain single-line text). Only the publish gate (AB#6387) reports it: the compiler
    ///     rejects the same entries at compile time (message 130).
    /// </summary>
    public const string InvalidEntryCode = "OCTO-CK206";

    /// <summary>
    ///     One finding per unacknowledged change (<c>OCTO-CK203</c>) and per stale entry (<c>OCTO-CK204</c>), without
    ///     the code prefix.
    /// </summary>
    /// <param name="result">The acknowledgement result of the verdict.</param>
    /// <param name="modelName">The model name for the texts.</param>
    /// <param name="publishedRelease">
    ///     AB#7894: the published release of exactly the declared version when the verdict compares the model with it
    ///     (<see cref="CkPublishedRelease.IsDeclaredVersionPublished" />), otherwise null. A stale entry then belongs to
    ///     that release, and the next step is to raise the version, not to remove the entry: removing it without a raise
    ///     changes the content of a published version (<c>OCTO-CK205</c>).
    /// </param>
    public static IReadOnlyList<(string Code, string Text)> GetFindings(CkAcknowledgementResult result,
        string modelName, CkModelId? publishedRelease = null)
    {
        var findings = new List<(string, string)>();
        foreach (var missing in result.Unacknowledged)
        {
            findings.Add((MissingCode,
                $"{CkModelChangeFormatter.Format(missing.Change.Change)} needs an explicit acknowledgement " +
                $"({missing.Change.Reason}) but model '{modelName}' does not acknowledge it. Add to ckModel.yaml, " +
                $"with a reason:{Environment.NewLine}{CkChangeKey.ExampleEntry(missing.Change.Change)}"));
        }

        foreach (var stale in result.Stale)
        {
            if (publishedRelease != null)
            {
                findings.Add((StaleCode,
                    $"The acknowledgement \"{stale.Change}\" of model '{modelName}' matches no change against " +
                    $"'{publishedRelease.FullName}', which is already published under the declared version: the entry " +
                    "belongs to that release. Raise the version in ckModel.yaml first; with the raised version, remove the " +
                    "entry if it matches no change of the new release. Do not remove it without raising the version: a " +
                    "published version is immutable (OCTO-CK205)."));
                continue;
            }

            findings.Add((StaleCode, stale.MatchedChange == null
                ? $"The acknowledgement \"{stale.Change}\" of model '{modelName}' matches no change in this release. " +
                  "An acknowledgement is valid for one release only; remove the entry from compatibility.acknowledge."
                : $"The acknowledgement \"{stale.Change}\" of model '{modelName}' refers to " +
                  $"'{CkModelChangeFormatter.Format(stale.MatchedChange.Change)}', which needs no acknowledgement. " +
                  "An acknowledgement does not waive a version bump; remove the entry from compatibility.acknowledge."));
        }

        return findings;
    }

    /// <summary>
    ///     The lines of the "Acknowledged changes" section: change, level and reason.
    /// </summary>
    public static IReadOnlyList<string> GetAcknowledgedLines(CkAcknowledgementResult result) =>
        result.Acknowledged.Select(a =>
                $"{CkModelChangeFormatter.GetLevelLabel(a.Change.Level)}  {CkModelChangeFormatter.Format(a.Change.Change)} " +
                $"— acknowledged: {a.Reason}")
            .ToList();

    /// <summary>
    ///     One <c>OCTO-CK206</c> finding per violated rule of the entries of a compiled model (hand-edited or forged
    ///     JSON; AB#6387). The rules are those of the compiler, <see cref="CkAcknowledgeEntryRules" />.
    /// </summary>
    public static IReadOnlyList<(string Code, string Text)> GetInvalidEntryFindings(
        IReadOnlyList<CkAcknowledgeDto>? entries, string modelName) =>
        entries == null
            ? []
            : CkAcknowledgeEntryRules.FindProblems(entries)
                .Select(p => (InvalidEntryCode,
                    $"Entry {p.Index} of 'compatibility.acknowledge' in the compiled model '{modelName}' is invalid: " +
                    $"{p.Problem}. The compiled file is not trusted; recompile it from a valid ckModel.yaml."))
                .ToList();
}
