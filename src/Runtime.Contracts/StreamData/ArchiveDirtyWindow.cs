using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Information A of the recompute model (AB#4184): a window on a source archive whose data changed
/// retroactively. Carries the affected half-open interval <c>[WindowStart, WindowEnd)</c> plus how
/// (<see cref="ChangeKind"/>) and through which path (<see cref="Source"/>) the change arrived.
/// Maps 1:1 to the <c>CkArchiveDirtyWindow</c> record. All timestamps are UTC.
/// </summary>
public sealed record ArchiveDirtyWindow(
    DateTime WindowStart,
    DateTime WindowEnd,
    RecomputeChangeKind ChangeKind,
    RecomputeChangeSource Source,
    DateTime DetectedAt)
{
    /// <summary>
    /// The rtIds whose rows made the window dirty (AB#9016, System.StreamData 1.26.0), or <c>null</c> when the window
    /// covers every series - a window recorded before 1.26.0, a manual write, or a write that touched more rtIds than
    /// <see cref="RecomputeLateDataOptions.MaxScopedRtIds"/>. The dependent rollups refresh only these series.
    /// </summary>
    /// <remarks>
    /// An init-only property instead of a positional parameter on purpose: the positional constructor keeps its
    /// signature, so a storage layer compiled against the previous contract still runs against this one (it simply
    /// records no set, i.e. the unscoped behaviour).
    /// </remarks>
    public RtIdSet? RtIds { get; init; }
}
