using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Persistence operations for <c>TimeRangeArchive</c> entities. Mirrors
/// <see cref="IArchiveRuntimeStore"/> for the time-range-specific subtype: read/get the
/// archive's metadata snapshot and the soft-delete entry point. There is no <c>InsertAsync</c>
/// counterpart on this store — data inserts go through
/// <see cref="IStreamDataRepository.InsertTimeRangeAsync"/> directly (CrateDB writes), not via
/// Mongo. The store is the Mongo side: schema definition, status transitions (inherited from
/// the archive lifecycle), soft-delete.
/// </summary>
/// <remarks>
/// Concept doc: <c>docs/concept-time-range-archives.md</c> §3, §4. Time-range archives have no
/// orchestrator and no source-archive coupling, so the interface is intentionally smaller
/// than <see cref="IRollupArchiveRuntimeStore"/>.
/// </remarks>
public interface ITimeRangeArchiveRuntimeStore
{
    /// <summary>
    /// Returns the snapshot of the time-range archive identified by <paramref name="archiveRtId"/>,
    /// or <c>null</c> if no such entity exists (or has been soft-deleted). The shared
    /// <see cref="IArchiveRuntimeStore.GetAsync"/> on the base store also returns a snapshot for
    /// this entity (Mongo polymorphism); this method exists for callers that explicitly want the
    /// time-range view and want to fail fast if the archive is a different subtype.
    /// </summary>
    Task<ArchiveSnapshot?> GetAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Creates a new <c>TimeRangeArchive</c> entity in <see cref="CkArchiveStatus.Created"/>. The
    /// archive is not yet provisioned on CrateDB; the standard
    /// <see cref="IArchiveLifecycleService.ActivateAsync"/> path drives the DDL emission. Returns
    /// the generated runtime id. Time-range concept §3, §10.
    /// </summary>
    /// <param name="rtWellKnownName">Optional human-readable name; null falls back to the rtId.</param>
    /// <param name="targetCkTypeId">CK type whose rows this archive captures windowed values for.</param>
    /// <param name="columns">User-picked attribute paths that become CrateDB storage columns.</param>
    /// <param name="period">
    /// Advisory period for the archive's windows (e.g. 15 min, 1 h). Optional and descriptive only —
    /// the engine does not enforce that incoming windows match the declared period.
    /// </param>
    Task<OctoObjectId> InsertAsync(
        string? rtWellKnownName,
        RtCkId<CkTypeId> targetCkTypeId,
        System.Collections.Generic.IReadOnlyList<CkArchiveColumnSpec> columns,
        System.TimeSpan? period);

    /// <summary>
    /// Soft-deletes the time-range archive entity by setting <c>rtState = Archived</c>. The Crate
    /// table is dropped separately by the lifecycle service via
    /// <see cref="IStreamDataRepository.DeleteArchiveAsync"/>.
    /// </summary>
    Task ArchiveEntityAsync(OctoObjectId archiveRtId);
}
