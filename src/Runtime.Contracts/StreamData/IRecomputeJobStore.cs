using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Persists the <c>RecomputeJob</c> history (AB#4184): one entity per recompute run, queryable per
/// archive so an operator can debug why a recompute failed. Separate from
/// <see cref="IArchiveRecomputeStateStore"/> because jobs are their own prunable entities, not
/// attributes on the archive.
/// </summary>
public interface IRecomputeJobStore
{
    /// <summary>
    /// Persists a new job (its <see cref="RecomputeJobSnapshot.RtId"/> is ignored) and returns the
    /// assigned runtime id.
    /// </summary>
    Task<OctoObjectId> CreateAsync(RecomputeJobSnapshot job);

    /// <summary>
    /// Overwrites the stored job identified by <see cref="RecomputeJobSnapshot.RtId"/> with the given
    /// state — used to advance Pending → Running → Swapping → Completed/Failed and to record counts,
    /// timings, and the failure reason.
    /// </summary>
    Task UpdateAsync(RecomputeJobSnapshot job);

    /// <summary>Reads a single job by id, or <c>null</c> if it does not exist.</summary>
    Task<RecomputeJobSnapshot?> GetAsync(OctoObjectId jobRtId);

    /// <summary>
    /// Returns the most recent jobs for an archive, newest first, capped at <paramref name="limit"/>.
    /// Backs the <c>recomputeJobsFor(archiveRtId)</c> query.
    /// </summary>
    Task<IReadOnlyList<RecomputeJobSnapshot>> GetForArchiveAsync(OctoObjectId archiveRtId, int limit);

    /// <summary>
    /// Returns the single non-terminal job (Pending / Running / Swapping) for an archive, or
    /// <c>null</c> if none is active. Used by the coalesce policy to fold a new trigger into an
    /// already-running job instead of starting a second one.
    /// </summary>
    Task<RecomputeJobSnapshot?> GetActiveForArchiveAsync(OctoObjectId archiveRtId);
}
