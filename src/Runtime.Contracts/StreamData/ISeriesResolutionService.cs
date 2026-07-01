using System.Threading;
using System.Threading.Tasks;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolution-aware series resolver (AB#4290): given a logical series' base archive, a query window
/// and a target point count, decides which archive in the series' resolution family (base + rollups)
/// to query and at what effective resolution — without the caller knowing which physical archive
/// holds the data at a usable grain. It picks the route; it does not run the query.
/// See <c>concept-resolution-aware-series-queries.md</c> §5.1.
/// </summary>
/// <remarks>
/// Constructed per-tenant with the tenant's archive / rollup stores (like
/// <see cref="IRollupDependencyGraph"/>), so callers wire it from their tenant context.
/// </remarks>
public interface ISeriesResolutionService
{
    /// <summary>
    /// Resolves the archive to query for <paramref name="request"/>. Never throws for a
    /// business-level "no suitable route" outcome — those are carried by
    /// <see cref="SeriesResolutionResult.Signal"/>.
    /// </summary>
    Task<SeriesResolutionResult> ResolveAsync(
        SeriesResolutionRequest request,
        CancellationToken cancellationToken = default);
}
