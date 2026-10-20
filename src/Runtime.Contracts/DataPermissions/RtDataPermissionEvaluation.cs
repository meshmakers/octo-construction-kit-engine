using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Contracts.DataPermissions;

/// <summary>
///     Outcome of a data-permission check (AB#7035). The three outcomes are deliberately distinct so a
///     caller can map them differently: <see cref="NotVisible" /> must be answered exactly like a missing
///     entity (not found), <see cref="NotPermitted" /> like a refusal.
/// </summary>
public enum RtDataPermissionDecision
{
    /// <summary>The caller may perform the action.</summary>
    Allowed,

    /// <summary>
    ///     The caller cannot read the entity (or, for a type-level read check, the type). To the caller the
    ///     entity does not exist — answer "not found", never "forbidden".
    /// </summary>
    NotVisible,

    /// <summary>The entity is visible to the caller but the action is not permitted.</summary>
    NotPermitted
}

/// <summary>
///     Language-neutral reason code of an <see cref="RtDataPermissionVerdict" />. For machine use and
///     logging only; never part of a user-facing text.
/// </summary>
public enum RtDataPermissionReason
{
    /// <summary>No enforcing policy applies (system caller, no policies, or unprotected type).</summary>
    Open,

    /// <summary>A role of the caller holds a full-scope grant for the action.</summary>
    Granted,

    /// <summary>An owned-only grant applies and the caller owns the entity (or, type level, may create).</summary>
    GrantedAsOwner,

    /// <summary>
    ///     An AuditOnly policy would have refused the caller; the action passes and
    ///     <see cref="RtDataPermissionVerdict.AuditOnlyViolation" /> is set.
    /// </summary>
    AuditOnly,

    /// <summary>The type is protected and no role of the caller holds a grant for the action.</summary>
    NoGrant,

    /// <summary>An owned-only grant applies but the caller is not the owner of the entity.</summary>
    NotOwner,

    /// <summary>No entity with the given id is stored (answered as <see cref="RtDataPermissionDecision.NotVisible" />).</summary>
    EntityNotFound,

    /// <summary>The entity is locked by a blueprint and the type opted into blueprint-lock protection.</summary>
    BlueprintLocked
}

/// <summary>
///     Result of a data-permission check. Carries no entity data.
/// </summary>
/// <param name="Decision">The verdict</param>
/// <param name="Reason">Language-neutral reason code</param>
/// <param name="AuditOnlyViolation">
///     True when an AuditOnly policy would have refused the caller (decision stays
///     <see cref="RtDataPermissionDecision.Allowed" />)
/// </param>
public readonly record struct RtDataPermissionVerdict(
    RtDataPermissionDecision Decision,
    RtDataPermissionReason Reason,
    bool AuditOnlyViolation = false)
{
    /// <summary>True when <see cref="Decision" /> is <see cref="RtDataPermissionDecision.Allowed" />.</summary>
    public bool IsAllowed => Decision == RtDataPermissionDecision.Allowed;
}

/// <summary>
///     The one public question "may this caller read / write / delete this entity (or type)?" (AB#7035).
///     Implemented on exactly the rules the repository enforces: reads are filtered by the same
///     classification (<see cref="RtDataAccessEvaluator" />) and writes are guarded by the same classification,
///     ownership and blueprint-lock code, so the verdict equals what a session bound to the same
///     <see cref="RtSecurityContext" /> would experience. Side-effect free unless <c>publishAudit</c> is set.
/// </summary>
public interface IRtDataPermissionEvaluator
{
    /// <summary>
    ///     Evaluates the action for one stored entity. A caller that cannot read the entity gets
    ///     <see cref="RtDataPermissionDecision.NotVisible" /> for every action (also when the entity does not
    ///     exist). Write and Delete additionally need the respective grant (and ownership for owned-only
    ///     grants, and an unlocked entity when the type opted into blueprint-lock protection).
    /// </summary>
    /// <param name="runtimeRepository">Repository of the tenant</param>
    /// <param name="caller">The caller; <see cref="RtSecurityContext.System" /> is always allowed</param>
    /// <param name="target">Type and id of the entity</param>
    /// <param name="action">Read, Write or Delete</param>
    /// <param name="publishAudit">
    ///     True to publish the same audit event as the repository when an AuditOnly policy would have
    ///     refused the caller; false (default) keeps the call free of side effects
    /// </param>
    /// <param name="cancellationToken">Cancellation</param>
    Task<RtDataPermissionVerdict> EvaluateAsync(IRuntimeRepository runtimeRepository, RtSecurityContext caller,
        RtEntityId target, RtDataAction action, bool publishAudit = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Evaluates the action for a type alone (no entity, e.g. a static method). Read: a type the caller
    ///     cannot read at all is <see cref="RtDataPermissionDecision.NotVisible" />; an owned-only grant is
    ///     allowed (individual entities are decided by the entity-level call). Write/Delete: a missing grant is
    ///     <see cref="RtDataPermissionDecision.NotPermitted" />; an owned-only grant is allowed like the
    ///     generic insert (there is no entity to own yet).
    /// </summary>
    /// <param name="runtimeRepository">Repository of the tenant</param>
    /// <param name="caller">The caller</param>
    /// <param name="ckTypeId">The type</param>
    /// <param name="action">Read, Write or Delete</param>
    /// <param name="publishAudit">See <see cref="EvaluateAsync" /></param>
    /// <param name="cancellationToken">Cancellation</param>
    Task<RtDataPermissionVerdict> EvaluateTypeAsync(IRuntimeRepository runtimeRepository, RtSecurityContext caller,
        RtCkId<CkTypeId> ckTypeId, RtDataAction action, bool publishAudit = false,
        CancellationToken cancellationToken = default);
}
