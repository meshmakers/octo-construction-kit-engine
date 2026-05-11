using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Abstracts the persistence operations on <c>CkRollupArchive</c> entities required by the rollup
/// orchestrator and the lifecycle service. Decouples those services from any particular runtime
/// repository implementation (Mongo today, possibly others later) and from the generated
/// <c>CkRollupArchive</c> class itself, so the orchestrator stays in <c>Runtime.Engine</c> without
/// taking a hard dependency on the StreamData CK model package.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ICkArchiveRuntimeStore"/> for the rollup-specific entity. Implementations
/// must reject schema-relevant mutations once the rollup has left
/// <see cref="CkArchiveStatus.Created"/> (concept §7) and must maintain
/// <see cref="CkRollupArchiveSnapshot.LastAggregatedBucketEnd"/> and
/// <see cref="CkRollupArchiveSnapshot.FrozenUntil"/> as monotonic where the concept requires it
/// (§6).
/// </remarks>
public interface ICkRollupArchiveRuntimeStore
{
    /// <summary>
    /// Reads the current state of the rollup identified by <paramref name="rollupRtId"/>, or
    /// <c>null</c> if no such entity exists (or has been soft-deleted).
    /// </summary>
    Task<CkRollupArchiveSnapshot?> GetAsync(OctoObjectId rollupRtId);

    /// <summary>
    /// Inserts a new CkRollupArchive entity in <see cref="CkArchiveStatus.Created"/>. The shared
    /// archive lifecycle service handles status transitions afterwards (Activate, Disable, etc.);
    /// this method exists separately so the lifecycle service can derive the inherited CkArchive
    /// attributes (<paramref name="targetCkTypeId"/>, <paramref name="columns"/>) from the source
    /// archive and the aggregations, keeping the generic CkEntity GraphQL mutation pipeline free
    /// of rollup-specific knowledge. Concept §4, §9.
    /// </summary>
    /// <param name="rtWellKnownName">Optional human-readable name. Null falls back to the rtId.</param>
    /// <param name="targetCkTypeId">CK type the rollup rows live on — inherited from the source archive.</param>
    /// <param name="sourceArchiveRtId">RtId of the source CkArchive (or CkRollupArchive for chained rollups).</param>
    /// <param name="bucketSize">Bucket width.</param>
    /// <param name="watermarkLag">How long the orchestrator waits after bucket-end before aggregating.</param>
    /// <param name="aggregations">User-defined aggregation specs.</param>
    /// <param name="columns">
    /// Derived storage columns (from <see cref="RollupColumnGenerator.Generate"/>). Stored on the
    /// inherited Columns slot for mandatory-attribute validation; the read path re-derives from
    /// <paramref name="aggregations"/> to stay authoritative.
    /// </param>
    /// <returns>The generated runtime id of the new rollup archive.</returns>
    Task<OctoObjectId> InsertAsync(
        string? rtWellKnownName,
        RtCkId<CkTypeId> targetCkTypeId,
        OctoObjectId sourceArchiveRtId,
        TimeSpan bucketSize,
        TimeSpan watermarkLag,
        IReadOnlyList<CkRollupAggregationSpec> aggregations,
        IReadOnlyList<CkArchiveColumnSpec> columns);

    /// <summary>
    /// Soft-deletes the rollup entity by setting <c>rtState = Archived</c>. The Crate table is
    /// dropped separately by the lifecycle service via <see cref="IStreamDataRepository"/>.
    /// </summary>
    Task ArchiveEntityAsync(OctoObjectId rollupRtId);

    /// <summary>
    /// Advances <see cref="CkRollupArchiveSnapshot.LastAggregatedBucketEnd"/> to
    /// <paramref name="bucketEnd"/>. Called by the orchestrator immediately after a bucket's rows
    /// have been upserted into the rollup table. Implementations should reject backwards moves
    /// unless <paramref name="allowRewind"/> is true (used by the <c>rewindRollupWatermark</c>
    /// mutation).
    /// </summary>
    Task AdvanceWatermarkAsync(OctoObjectId rollupRtId, DateTime bucketEnd, bool allowRewind = false);

    /// <summary>
    /// Sets <see cref="CkRollupArchiveSnapshot.FrozenUntil"/>. Implementations enforce monotonicity:
    /// the new value must be greater than or equal to the current one, except when the
    /// <c>unfreezeRollupArchive</c> mutation is called (which passes <c>null</c>). Concept §6.
    /// </summary>
    Task SetFrozenUntilAsync(OctoObjectId rollupRtId, DateTime? frozenUntil);

    /// <summary>
    /// Enumerates every non-soft-deleted CkRollupArchive entity in the tenant. Used by the rollup
    /// orchestrator background worker on each tick to pick up rollups that are due for processing.
    /// Order is implementation-defined; callers must not rely on it.
    /// </summary>
    IAsyncEnumerable<CkRollupArchiveSnapshot> EnumerateAsync();

    /// <summary>
    /// Returns the count of non-soft-deleted rollups that reference
    /// <paramref name="sourceArchiveRtId"/>. Used by the source archive's delete path to enforce
    /// <see cref="RollupSourceInUseException"/> (concept §6).
    /// </summary>
    Task<int> CountActiveRollupsForSourceAsync(OctoObjectId sourceArchiveRtId);
}
