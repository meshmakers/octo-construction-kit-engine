using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IRollupDependencyGraph"/>. Builds the reverse adjacency
/// (sourceArchiveRtId → its direct rollups) from a single enumeration of the tenant's rollups — one
/// entry per (source, rollup) pair, so a multi-source rollup (AB#5157) hangs off <em>each</em> of
/// its sources — then walks it breadth-first from the requested source so the result is naturally
/// top-down (a parent rollup precedes its rollup-of-rollup children). One enumeration per call keeps
/// the cost linear in the number of edges; the graph is small (one node per rollup) so this is cheap
/// relative to the recompute work it gates.
/// </summary>
public sealed class RollupDependencyGraph : IRollupDependencyGraph
{
    private readonly IRollupArchiveRuntimeStore _rollupStore;

    /// <summary>
    /// Creates the dependency graph over the given rollup store, whose
    /// <see cref="IRollupArchiveRuntimeStore.EnumerateAsync"/> supplies the rollup → source edges.
    /// </summary>
    public RollupDependencyGraph(IRollupArchiveRuntimeStore rollupStore)
    {
        _rollupStore = rollupStore;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RollupArchiveSnapshot>> GetTransitiveDependentsAsync(
        OctoObjectId sourceArchiveRtId)
    {
        // sourceRtId → rollups that aggregate directly from it. A rollup declaring N sources is a
        // direct dependent of every one of them, whatever validity span each reference carries; a
        // source listed twice (rejected by the validator, but tolerated here) yields one edge.
        var bySource = new Dictionary<OctoObjectId, List<RollupArchiveSnapshot>>();
        await foreach (var rollup in _rollupStore.EnumerateAsync())
        {
            var linkedSources = new HashSet<OctoObjectId>();
            foreach (var source in rollup.Sources)
            {
                if (!linkedSources.Add(source.SourceArchiveRtId))
                {
                    continue;
                }

                if (!bySource.TryGetValue(source.SourceArchiveRtId, out var list))
                {
                    list = new List<RollupArchiveSnapshot>();
                    bySource[source.SourceArchiveRtId] = list;
                }

                list.Add(rollup);
            }
        }

        var result = new List<RollupArchiveSnapshot>();
        var visited = new HashSet<OctoObjectId>();
        var queue = new Queue<OctoObjectId>();
        queue.Enqueue(sourceArchiveRtId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!bySource.TryGetValue(current, out var dependents))
            {
                continue;
            }

            foreach (var dependent in dependents)
            {
                // A rollup is its own node; visiting it once guards against diamonds (two paths to
                // the same rollup, or one two-source rollup reachable via both parents) and against
                // any cycle the model failed to reject.
                if (!visited.Add(dependent.RtId))
                {
                    continue;
                }

                result.Add(dependent);
                queue.Enqueue(dependent.RtId);
            }
        }

        return result;
    }
}
