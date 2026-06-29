using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Abstracts the persistence operations on <c>CkArchive</c> entities required by
/// <see cref="IArchiveLifecycleService"/>. Decouples the lifecycle state machine from any
/// particular runtime repository implementation (Mongo today, possibly others later) and from the
/// generated <c>CkArchive</c> class itself, so the service stays in <c>Runtime.Engine</c> without
/// taking a hard dependency on the StreamData CK model package.
/// </summary>
public interface IArchiveRuntimeStore
{
    /// <summary>
    /// Reads the current state of the archive identified by <paramref name="archiveRtId"/>, or
    /// <c>null</c> if no such entity exists (or has been soft-deleted).
    /// </summary>
    Task<ArchiveSnapshot?> GetAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Writes a new <see cref="CkArchiveStatus"/> on the archive entity. Implementations are
    /// responsible for any validation hooks (concept §10) and for emitting the corresponding
    /// status-transition event (§14) only if the lifecycle service hasn't already done so via
    /// <see cref="IArchiveAuditTrail"/>.
    /// </summary>
    Task SetStatusAsync(OctoObjectId archiveRtId, CkArchiveStatus newStatus);

    /// <summary>
    /// Soft-deletes the archive entity by setting <c>rtState = Archived</c>. The Crate table is
    /// dropped separately by the lifecycle service via <see cref="IStreamDataRepository"/>.
    /// </summary>
    Task ArchiveEntityAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Enumerates every non-soft-deleted CkArchive entity in the tenant. Used by the startup
    /// reconciliation job (concept §11) to compare Mongo state against CrateDB reality. Order is
    /// implementation-defined; callers must not rely on it.
    /// </summary>
    IAsyncEnumerable<ArchiveSnapshot> EnumerateAsync();

    /// <summary>
    /// Appends a computed column to the archive's column set (AB#4189 Phase 7). The caller supplies
    /// the column already carrying its initial <see cref="CkArchiveColumnSpec.ComputedState"/>
    /// (normally <see cref="ComputedColumnState.Pending"/> for an add to a live archive). Used by the
    /// computed-column lifecycle on <see cref="IArchiveLifecycleService"/>.
    /// </summary>
    Task AddComputedColumnAsync(OctoObjectId archiveRtId, CkArchiveColumnSpec column);

    /// <summary>
    /// Sets the engine-managed lifecycle <see cref="ComputedColumnState"/> of the computed column
    /// named <paramref name="name"/> on the archive (AB#4189 Phase 7). This single write is the
    /// atomic switch for the add flow: while the state is <c>Pending</c> / <c>Backfilling</c> the
    /// read path hides the column; flipping it to <c>Active</c> exposes the backfilled values.
    /// </summary>
    Task SetComputedColumnStateAsync(OctoObjectId archiveRtId, string name, ComputedColumnState state);

    /// <summary>
    /// Removes the computed column named <paramref name="name"/> from the archive's column set
    /// (AB#4189 Phase 7). The physical CrateDB column is left in place (a harmless nullable orphan
    /// the read path no longer projects); a later re-add of the same name reuses it.
    /// </summary>
    Task RemoveComputedColumnAsync(OctoObjectId archiveRtId, string name);

    /// <summary>
    /// Sets the transient <c>PendingFormula</c> marker on a computed column (AB#4189 Phase 7),
    /// starting a formula change: the column keeps serving its current formula while ingest begins
    /// dual-writing the pending formula into the versioned physical column. The column stays
    /// <c>Active</c> throughout.
    /// </summary>
    Task SetPendingFormulaAsync(OctoObjectId archiveRtId, string name, string pendingFormula);

    /// <summary>
    /// Atomically swaps a computed column to its new formula once the backfill completes (AB#4189
    /// Phase 7): sets <c>Formula = newFormula</c>, <c>ComputedVersion = newVersion</c>, and clears
    /// <c>PendingFormula</c> in one write. After this the read path resolves the column to the new
    /// versioned physical column.
    /// </summary>
    Task SwapComputedColumnFormulaAsync(OctoObjectId archiveRtId, string name, string newFormula, int newVersion);

    /// <summary>
    /// Clears the <c>PendingFormula</c> marker without swapping (AB#4189 Phase 7), reverting a failed
    /// formula change: the column keeps its previous formula / version and ingest stops dual-writing.
    /// The orphaned pending physical column is left in place.
    /// </summary>
    Task ClearPendingFormulaAsync(OctoObjectId archiveRtId, string name);
}
