namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
///     Who initiated an RT import and which restrictions apply to it (AB#6392). RT imports run on a system
///     session (replace semantics, creator stamping and the preserve pass stay as they are), so the identity of
///     a user who triggered the import through the API is lost on the way through the job queue. This context
///     carries the initiating caller into <see cref="IImportRtModelCommand" /> as a narrow flag: it adds the
///     blueprint-lock protection of AB#6384 and nothing else. Blueprint install/update, forced re-apply and
///     CK-model migrations call the command without a context and are unchanged.
/// </summary>
public sealed record RtImportCallerContext
{
    /// <summary>
    ///     Subject id of the initiating caller (user <c>sub</c> claim or client id). Null when the queue message
    ///     carried none (e.g. produced by an older service) — the protection is applied nevertheless.
    /// </summary>
    public string? SubjectId { get; init; }

    /// <summary>
    ///     True when the import was initiated by a user or client through an API route: entities of CK types that
    ///     opted in to the blueprint-lock protection (data policy attribute <c>ProtectBlueprintLocked</c>) cannot
    ///     be overwritten when they are locked by a blueprint, and the blueprint bookkeeping attributes of the
    ///     file are stripped. System flows leave this false.
    /// </summary>
    public bool EnforceBlueprintLock { get; init; }

    /// <summary>
    ///     Creates the context of an import initiated by a user or client through an API route. A missing subject
    ///     never turns the import into a system import: the protection is applied regardless.
    /// </summary>
    /// <param name="subjectId">Subject id of the caller, if known</param>
    public static RtImportCallerContext UserInitiated(string? subjectId)
    {
        return new RtImportCallerContext { SubjectId = subjectId, EnforceBlueprintLock = true };
    }
}

/// <summary>
///     What the blueprint-lock protection did during user-initiated imports of one command instance (AB#6392).
/// </summary>
/// <param name="StrippedAttributeCount">
///     Number of blueprint bookkeeping attribute values (<c>RtBlueprintLocked</c>, <c>RtBlueprintSource</c>,
///     <c>RtBlueprintAppliedAt</c>) removed from incoming entities of opted-in types.
/// </param>
/// <param name="StrippedEntityCount">Number of incoming entities that carried at least one such value.</param>
/// <param name="AuditedViolationCount">
///     Number of incoming entities that overwrote a blueprint-locked entity under an AuditOnly policy
///     (written, audited, not blocked).
/// </param>
public sealed record RtImportBlueprintLockSummary(int StrippedAttributeCount, int StrippedEntityCount,
    int AuditedViolationCount)
{
    /// <summary>Nothing stripped, nothing audited.</summary>
    public static readonly RtImportBlueprintLockSummary Empty = new(0, 0, 0);

    /// <summary>True when the import produced a warning-worthy result.</summary>
    public bool HasFindings => StrippedAttributeCount > 0 || AuditedViolationCount > 0;
}
