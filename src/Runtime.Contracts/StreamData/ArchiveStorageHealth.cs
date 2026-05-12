namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Backend-agnostic health classification for an archive's underlying storage table. Concrete
/// data-store providers (CrateDB today; other time-series engines in the future) map their native
/// health signal — replica state, sharding state, missing partitions — onto this small enum so
/// callers can render a uniform badge without leaking backend vocabulary.
/// </summary>
public enum ArchiveStorageHealth
{
    /// <summary>
    /// The provider could not determine a health state (e.g. the backing table doesn't exist yet,
    /// or the introspection query failed). UI should render this distinctly from
    /// <see cref="Good"/> rather than treating it as a green state.
    /// </summary>
    Unknown = 0,

    /// <summary>All shards / replicas / partitions are present and reachable.</summary>
    Good = 1,

    /// <summary>
    /// The table is operational but degraded — e.g. some replicas are unassigned or rebalancing
    /// is in progress. Reads and writes still work; operator attention is advisable but not
    /// urgent.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// One or more primary shards / partitions are missing. The table may be returning incomplete
    /// query results or rejecting writes. Operator action required.
    /// </summary>
    Critical = 3,
}
