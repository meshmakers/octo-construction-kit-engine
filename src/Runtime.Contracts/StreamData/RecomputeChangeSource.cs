namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Through which path a retroactive archive change arrived (AB#4184). Kept separately from
/// <see cref="RecomputeChangeKind"/> so the audit trail can distinguish a manual operator edit from
/// an automated pipeline re-ingest or a bulk import. Mirrors the <c>CkRecomputeChangeSource</c> CK
/// enum (System.StreamData ≥ 1.6.0); key values match so the snapshot maps by a direct cast.
/// </summary>
public enum RecomputeChangeSource
{
    /// <summary>An operator changed the data directly (manual correction).</summary>
    Manual = 0,

    /// <summary>A mesh-adapter pipeline re-ingested or corrected the data.</summary>
    Pipeline = 1,

    /// <summary>A bulk archive-data import wrote the change.</summary>
    Import = 2
}
