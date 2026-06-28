namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// How a write touched an archive window, used to drive recompute of derived archives (AB#4184).
/// Mirrors the <c>CkRecomputeChangeKind</c> CK enum (System.StreamData ≥ 1.6.0); key values match
/// so the snapshot maps by a direct cast.
/// </summary>
public enum RecomputeChangeKind
{
    /// <summary>
    /// A forward write at or after the high-water mark already consumed by dependents. Does not, by
    /// itself, make a dependent stale.
    /// </summary>
    Append = 0,

    /// <summary>
    /// A write into a window at or before the consumed high-water mark (correction, late value,
    /// re-ingest). Marks the covering window dirty so dependents are recomputed.
    /// </summary>
    RetroactiveModify = 1
}
