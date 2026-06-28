namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Engine-managed lifecycle state of a computed column on an archive. Mirrors the
/// <c>CkComputedColumnState</c> CK enum (System.StreamData ≥ 1.5.0); key values match so the
/// snapshot maps by a direct cast.
/// </summary>
public enum ComputedColumnState
{
    /// <summary>A backfill is scheduled but not started; consumers still see the previous state.</summary>
    Pending = 0,

    /// <summary>The backfill is running; consumers still see the previous state until it commits.</summary>
    Backfilling = 1,

    /// <summary>The computed column is live — new rows include the value and readers see it.</summary>
    Active = 2,

    /// <summary>The backfill failed; the previous state is intact, no partial data is visible.</summary>
    Failed = 3
}
