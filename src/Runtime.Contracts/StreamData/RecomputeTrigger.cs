namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// What initiated an archive recompute run (AB#4184). Recorded on each
/// <see cref="RecomputeJobSnapshot"/> for observability. Mirrors the <c>CkRecomputeTrigger</c> CK
/// enum (System.StreamData ≥ 1.6.0); key values match so the snapshot maps by a direct cast.
/// </summary>
public enum RecomputeTrigger
{
    /// <summary>An operator forced the recompute via API / admin UI / octo-cli.</summary>
    Manual = 0,

    /// <summary>The recompute orchestrator picked up dirty windows on its scheduled tick.</summary>
    Periodic = 1,

    /// <summary>
    /// A successful recompute of an upstream archive marked this archive dirty and propagated
    /// downstream.
    /// </summary>
    ChainPropagation = 2
}
