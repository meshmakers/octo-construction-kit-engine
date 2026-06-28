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
    DateTime DetectedAt);
