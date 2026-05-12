using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Backend-agnostic snapshot of an archive's underlying storage table — row count, on-disk size,
/// and an overall health classification. Used by the studio's archives list to surface the live
/// state of each archive alongside its CK-side configuration.
/// </summary>
/// <remarks>
/// All values are reported by the data-store provider on demand; they are not cached on the
/// runtime entity. <see cref="TableExists"/> is <c>true</c> when the backing table is provisioned
/// — i.e. the archive has been activated and the DDL has landed. When false, <see cref="RecordCount"/>
/// and <see cref="SizeBytes"/> are both <c>0</c> and <see cref="Health"/> is
/// <see cref="ArchiveStorageHealth.Unknown"/>. Concrete providers may map their native health signal
/// (CrateDB <c>sys.health</c>, future TSDB equivalents) onto <see cref="Health"/> but must not
/// leak backend vocabulary through this contract.
/// </remarks>
/// <param name="ArchiveRtId">Runtime id of the archive this snapshot describes.</param>
/// <param name="TableExists">
/// True when the data-store provider sees a backing table for the archive. False when the archive
/// has not been activated yet or the backing table is missing.
/// </param>
/// <param name="RecordCount">
/// Total number of stored rows on the table (primary copies only — replica copies are not counted
/// twice). 0 when <see cref="TableExists"/> is false.
/// </param>
/// <param name="SizeBytes">
/// On-disk size of the primary copies, in bytes. 0 when <see cref="TableExists"/> is false. UIs
/// typically format this human-readable (KiB / MiB / GiB).
/// </param>
/// <param name="Health">
/// Overall health classification. <see cref="ArchiveStorageHealth.Unknown"/> when the provider
/// could not determine a state.
/// </param>
public sealed record ArchiveStorageStats(
    OctoObjectId ArchiveRtId,
    bool TableExists,
    long RecordCount,
    long SizeBytes,
    ArchiveStorageHealth Health);
