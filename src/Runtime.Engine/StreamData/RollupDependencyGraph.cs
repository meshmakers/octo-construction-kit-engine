using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IRollupDependencyGraph"/>. Builds the reverse adjacency
/// (sourceArchiveRtId → its direct rollups) from a single enumeration of the tenant's rollups, then
/// walks it breadth-first from the requested source so the result is naturally top-down (a parent
/// rollup precedes its rollup-of-rollup children). One enumeration per call keeps the cost linear in
/// the number of rollups; the graph is small (one node per rollup) so this is cheap relative to the
/// recompute work it gates.
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
        // sourceRtId → rollups that aggregate directly from it.
        var bySource = new Dictionary<OctoObjectId, List<RollupArchiveSnapshot>>();
        await foreach (var rollup in _rollupStore.EnumerateAsync())
        {
            if (!bySource.TryGetValue(rollup.SourceArchiveRtId, out var list))
            {
                list = new List<RollupArchiveSnapshot>();
                bySource[rollup.SourceArchiveRtId] = list;
            }

            list.Add(rollup);
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
                // the same rollup) and against any cycle the model failed to reject.
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
