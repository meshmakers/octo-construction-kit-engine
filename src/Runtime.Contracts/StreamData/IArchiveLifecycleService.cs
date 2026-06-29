using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;

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

    /// <summary>
    /// Adds a computed column to an <c>Activated</c> raw or time-range archive and backfills it
    /// (AB#4189 Phase 7, §8). Validates the prospective column set, persists the column
    /// <c>Pending</c>, adds the physical column, backfills the existing rows while the column stays
    /// hidden, then flips it to <c>Active</c> atomically. A backfill failure leaves the column
    /// <c>Failed</c> and the previous archive state intact. Idempotent re-add of the same name reuses
    /// the orphaned physical column.
    /// </summary>
    Task AddComputedColumnAsync(
        OctoObjectId archiveRtId, string name, string formula, FormulaResultType resultType, bool indexed);

    /// <summary>
    /// Removes a computed column from an archive (AB#4189 Phase 7). Validates that no remaining
    /// computed column references it, then drops it from the logical column set; the physical CrateDB
    /// column is left as a harmless orphan the read path no longer projects.
    /// </summary>
    Task RemoveComputedColumnAsync(OctoObjectId archiveRtId, string name);
}
