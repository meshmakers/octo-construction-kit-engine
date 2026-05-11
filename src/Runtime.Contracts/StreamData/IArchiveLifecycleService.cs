using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Orchestrates state-machine transitions for <c>CkArchive</c> entities. DB-neutral: delegates
/// DDL to <see cref="IStreamDataRepository"/> and entity reads/writes to the runtime repository
/// stack. Status changes are audit-trailed via the existing event infrastructure.
/// </summary>
/// <remarks>
/// Transition rules (see streamdata-archive-concept §3, §11):
/// <list type="bullet">
///   <item><c>Created → Activated</c>: provision Crate table; on DDL failure transition to <c>Failed</c>.</item>
///   <item><c>Activated ↔ Disabled</c>: status only, no Crate side-effect.</item>
///   <item><c>Failed → Activated</c>: retry DDL; idempotent.</item>
///   <item>Delete from any state: drop the Crate table and soft-delete the entity (rtState = Archived).</item>
/// </list>
/// Operation order is always <em>Crate first, Mongo last</em> so retries converge after transient
/// Mongo failures without leaving inconsistent state visible to callers.
/// </remarks>
public interface IArchiveLifecycleService
{
    /// <summary>
    /// Activates the archive: provisions the Crate table and sets <c>status = Activated</c>.
    /// Allowed from <c>Created</c>, <c>Disabled</c>, and <c>Failed</c>. Re-validates all column
    /// paths against the current CK model before any DDL runs.
    /// </summary>
    Task ActivateAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Sets <c>status = Disabled</c>. Allowed from <c>Activated</c>. The Crate table is preserved.
    /// </summary>
    Task DisableAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Sets <c>status = Activated</c> from <c>Disabled</c>. Re-validates column paths against the
    /// current CK model; performs no DDL because the table already exists.
    /// </summary>
    Task EnableAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Retries activation after a previous DDL failure. Allowed only from <c>Failed</c>; identical
    /// effect to <see cref="ActivateAsync"/>.
    /// </summary>
    Task RetryActivationAsync(OctoObjectId archiveRtId);

    /// <summary>
    /// Drops the Crate table (idempotent) and soft-deletes the <c>CkArchive</c> entity by setting
    /// <c>rtState = Archived</c>. Allowed from any status.
    /// </summary>
    Task DeleteAsync(OctoObjectId archiveRtId);
}
