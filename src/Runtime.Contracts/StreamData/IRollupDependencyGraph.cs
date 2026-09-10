using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolves the rollup dependency graph for the recompute model (AB#4184): which rollup archives
/// transitively depend on a given source archive. A raw / time-range archive may have N rollups
/// hanging off it, and each rollup may itself be the source of further rollups (rollup-of-rollup),
/// so a retroactive change on a base archive must propagate down a multi-level chain.
/// </summary>
/// <remarks>
/// Since AB#5157 the graph is a multi-parent DAG, not a tree: a rollup declares a list of sources
/// (<see cref="RollupArchiveSnapshot.Sources"/>) and is a direct dependent of <em>each</em> of
/// them, so the same rollup is reachable from several base archives and, in a diamond, along
/// several paths from one base. Traversal therefore dedups by rollup rtId — every dependent is
/// returned exactly once per query no matter how many edges lead to it.
/// </remarks>
public interface IRollupDependencyGraph
{
    /// <summary>
    /// Returns every rollup that transitively depends on <paramref name="sourceArchiveRtId"/> —
    /// direct dependents and their chained descendants — in top-down order (a parent always appears
    /// before its children) so callers can propagate a recompute downstream without re-sorting.
    /// The source archive itself is not included. Cycles (which the model forbids via
    /// <c>RollupCycleException</c>) are tolerated defensively: each rollup is visited at most once.
    /// </summary>
    Task<IReadOnlyList<RollupArchiveSnapshot>> GetTransitiveDependentsAsync(OctoObjectId sourceArchiveRtId);
}
