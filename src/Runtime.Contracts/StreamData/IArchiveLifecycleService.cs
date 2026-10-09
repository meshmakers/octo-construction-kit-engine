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
///   <item><c>Activated → Disabled</c>: status only, no Crate side-effect. <c>Disabled → Activated</c> runs the
///   idempotent provisioning again.</item>
///   <item><c>Activated → Activated</c>: no transition; the idempotent provisioning runs again so that a
///   column declared after the table was created is added to it. A failure is thrown to the caller and
///   leaves the archive <c>Activated</c>.</item>
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
    /// paths against the current CK model before any DDL runs. On an archive that is already
    /// <c>Activated</c> it changes no status and adds the declared columns the table does not have yet;
    /// if that fails, <see cref="ArchiveActivationFailedException"/> is thrown and the archive stays
    /// <c>Activated</c>.
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
    ///     CK v2 (review G3 E-M2): re-checks an archive against attribute access after a CK model change. When an
    ///     ingested column now reaches a Hidden attribute (directly or through a record), an activated archive is set
    ///     to <see cref="CkArchiveStatus.Failed" /> so it stops ingesting; returns <c>false</c>. Call it for the
    ///     archives of a tenant after a CK model import. Without a CK cache the check is skipped (<c>true</c>).
    /// </summary>
    Task<bool> RevalidateAccessAsync(OctoObjectId archiveRtId);

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

    /// <summary>
    /// Changes the formula of an existing computed column on an active archive with optimistic /
    /// atomic semantics (AB#4189 Phase 7, §8): readers keep seeing the previous formula's values
    /// while a backfill populates the new formula into a fresh versioned physical column, then the
    /// column-version pointer is swapped atomically. A backfill failure reverts to the previous
    /// formula. The result type is unchanged (a formula edit cannot retype the column). No-op when
    /// the formula is unchanged.
    /// </summary>
    Task UpdateComputedColumnFormulaAsync(OctoObjectId archiveRtId, string name, string formula);
}
