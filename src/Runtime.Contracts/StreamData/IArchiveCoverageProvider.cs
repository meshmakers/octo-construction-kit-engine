using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolves an archive's measured <see cref="ArchiveCoverage"/> (AB#5157) for a single tenant,
/// typically memoised so a family walk or a series resolution does not issue one storage round-trip
/// per rung on every request.
/// </summary>
/// <remarks>
/// Implementations are tenant-scoped: the tenant is bound when the provider is created, so callers
/// address archives by rtId alone. A cached implementation must cache the "no coverage" answer as
/// well (it is the normal state of a fresh archive) but must never cache an exception — a storage
/// failure propagates to the caller and is retried on the next request. The cache TTL is host
/// configurable; a query issued within the TTL right after a backfill still sees the pre-backfill
/// coverage (accepted, documented in <c>concept-multi-source-rollups.md</c> §7).
/// </remarks>
public interface IArchiveCoverageProvider
{
    /// <summary>
    /// Returns the measured coverage of the archive identified by <paramref name="archiveRtId"/>,
    /// or <c>null</c> when the archive holds no data (no backing table, or no rows) — never a
    /// sentinel range. An unknown rtId is answered with <c>null</c> the same way.
    /// </summary>
    /// <param name="archiveRtId">Runtime id of the archive to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ArchiveCoverage?> GetCoverageAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default);
}
