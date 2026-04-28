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
public interface ICkArchiveRuntimeStore
{
    /// <summary>
    /// Reads the current state of the archive identified by <paramref name="archiveRtId"/>, or
    /// <c>null</c> if no such entity exists (or has been soft-deleted).
    /// </summary>
    Task<CkArchiveSnapshot?> GetAsync(OctoObjectId archiveRtId);

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
    IAsyncEnumerable<CkArchiveSnapshot> EnumerateAsync();
}
