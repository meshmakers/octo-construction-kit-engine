using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     AB#7894: the rule for a model whose declared version is already published. Shared by the compile gate
///     (<see cref="CkCompileGate" />) and <c>octo-ckc ValidateVersion</c>, so both agree with the publish gate
///     (<see cref="CkPublishGate" />), which treats an equivalent re-publish of a published version as a no-op.
/// </summary>
/// <remarks>
///     A published version is a closed release: its compatibility, including its <c>compatibility.acknowledge</c>
///     entries, was judged when it was published. A rebuild of the same version with no structural change against the
///     published release (the CI rebuild of a released commit) is therefore not judged again; its acknowledgements are
///     neither stale (<c>OCTO-CK204</c>) nor missing (<c>OCTO-CK203</c>). A changed acknowledge block under a published
///     version is reported as a warning only: the publish gate keeps the published file as it is.
/// </remarks>
public static class CkPublishedRelease
{
    /// <summary>
    ///     True when the baseline is the published (non-local) entry of exactly the declared version.
    /// </summary>
    public static bool IsDeclaredVersionPublished(CkBaselineResolution resolution) =>
        resolution is { Baseline: not null, IsLocal: false, IsFromLowerMajor: false } &&
        resolution.Baseline.Version.CompareTo(resolution.DeclaredVersion) == 0;

    /// <summary>
    ///     True when the verdict compares the model with the published release of its own version and finds no change
    ///     that needs a version bump: a rebuild of the published release.
    /// </summary>
    public static bool IsRebuild(CkCompatibilityVerdict verdict) =>
        IsDeclaredVersionPublished(verdict.Resolution) && verdict.RequiredLevel == CkSemVerLevel.None;

    /// <summary>
    ///     True when both models carry the same acknowledge entries (change key and reason), independent of order.
    /// </summary>
    public static bool HaveSameAcknowledgements(CkCompiledModelRoot published, CkCompiledModelRoot current) =>
        Canonical(published.Compatibility?.Acknowledge).SequenceEqual(Canonical(current.Compatibility?.Acknowledge));

    /// <summary>
    ///     The information line of a rebuild of a published release.
    /// </summary>
    public static string FormatRebuildLine(CkCompatibilityVerdict verdict) =>
        $"Model '{verdict.BaselineId.FullName}' is already published ({verdict.Resolution.CatalogName ?? "unknown catalog"}) " +
        "and this build has no structural change against it: a rebuild of the published release. Its compatibility and " +
        "its acknowledgements were judged when it was published and are not judged again.";

    /// <summary>
    ///     The warning when the acknowledge entries of a rebuild differ from those of the published release.
    /// </summary>
    public static string FormatAcknowledgementsDifferLine(CkModelId published) =>
        $"The compatibility.acknowledge entries differ from those of the published release '{published.FullName}'. " +
        "A published version is immutable: publishing it again is a no-op and the published entries stay. Keep the " +
        "entries as released; with the next version raise, remove the entries that match no change of that release.";

    private static IEnumerable<(string Change, string Reason)> Canonical(IEnumerable<CkAcknowledgeDto>? entries) =>
        (entries ?? [])
        .Select(e => (e.Change.Trim(), e.Reason.Trim()))
        .OrderBy(e => e.Item1, StringComparer.Ordinal)
        .ThenBy(e => e.Item2, StringComparer.Ordinal);
}
