using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Records archive status transitions as audit events. Bridged at deployment time to the platform
/// notification/event repository (see concept §14); kept as a focused interface so the lifecycle
/// service has no direct dependency on the notifications package.
/// </summary>
public interface IArchiveAuditTrail
{
    /// <summary>
    /// Records a status transition. <paramref name="reason"/> carries the underlying error code
    /// or free-text reason for transitions to <see cref="CkArchiveStatus.Failed"/>; <c>null</c>
    /// for routine transitions.
    /// </summary>
    Task RecordTransitionAsync(
        OctoObjectId archiveRtId,
        CkArchiveStatus from,
        CkArchiveStatus to,
        string? reason);

    /// <summary>
    /// Records a deletion (any state → soft-deleted entity + dropped Crate table).
    /// </summary>
    Task RecordDeletionAsync(OctoObjectId archiveRtId, CkArchiveStatus statusAtDeletion);
}
