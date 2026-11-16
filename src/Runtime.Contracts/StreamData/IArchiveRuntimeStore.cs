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
    /// <see cref="IArchiveAuditTrail"/>. A store that keeps the status reason (AB#6247) clears
    /// <c>Archive.StatusReason</c> and <c>Archive.HiddenColumns</c> with this write.
    /// </summary>
    Task SetStatusAsync(OctoObjectId archiveRtId, CkArchiveStatus newStatus);

    /// <summary>
    /// Writes a new <see cref="CkArchiveStatus"/> together with its reason (AB#6247): <c>Archive.StatusReason</c> and
    /// <c>Archive.HiddenColumns</c> (the ingested column paths that reached a Hidden attribute). A <c>null</c> reason and
    /// an empty list clear both, which every transition other than the one to <see cref="CkArchiveStatus.Failed"/> does.
    /// The default implementation writes the status only (stores built before AB#6247 keep no reason).
    /// </summary>
    Task SetStatusAsync(OctoObjectId archiveRtId, CkArchiveStatus newStatus, string? statusReason,
        IReadOnlyList<string> hiddenColumns) => SetStatusAsync(archiveRtId, newStatus);

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
    /// Appends an <em>ingested</em> column (a CK attribute path, no formula) to the archive's column set (AB#7997).
    /// Called by <see cref="IArchiveLifecycleService.AddArchiveColumnAsync"/> only after the physical column exists,
    /// so the definition never names a column the table lacks. The default appends through
    /// <see cref="AddComputedColumnAsync"/>, which stores the column as given.
    /// </summary>
    Task AddIngestedColumnAsync(OctoObjectId archiveRtId, CkArchiveColumnSpec column) =>
        AddComputedColumnAsync(archiveRtId, column);

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
    /// Removes the <em>ingested</em> columns with the given paths (case-insensitive) from the archive's column set
    /// (AB#6247, <see cref="IArchiveLifecycleService.RedactHiddenColumnsAsync"/>). Called only after the physical columns
    /// were dropped, so the definition never names a column the table lacks. Computed columns are never removed here.
    /// The default implementation throws <see cref="System.NotSupportedException"/>.
    /// </summary>
    Task RemoveIngestedColumnsAsync(OctoObjectId archiveRtId, IReadOnlyCollection<string> columnPaths) =>
        throw new System.NotSupportedException(
            $"{GetType().Name} cannot remove ingested archive columns (AB#6247); update the archive store.");

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
