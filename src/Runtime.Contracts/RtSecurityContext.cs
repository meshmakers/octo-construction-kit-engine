namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Identity of the caller a repository session acts for. Carried on <see cref="IOctoSession" /> so the
///     engine can stamp <see cref="RepositoryEntities.RtEntity.RtCreatedBy" /> and, in later stages, enforce
///     data-level permissions on reads and writes (AB#4969).
/// </summary>
public sealed record RtSecurityContext
{
    /// <summary>
    ///     The system context: internal callers (pipelines, blueprint apply, migrations, background jobs).
    ///     System sessions stamp no creator and bypass data-level permission checks.
    /// </summary>
    public static readonly RtSecurityContext System = new() { IsSystem = true };

    /// <summary>
    ///     Subject id of the caller (user <c>sub</c> claim or client id); null for the system context.
    /// </summary>
    public string? SubjectId { get; init; }

    /// <summary>
    ///     Role names of the caller, as issued in the token's role claims.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];

    /// <summary>
    ///     True for internal callers acting without an end-user identity.
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>
    ///     AB#8574: what kind of principal the caller is (signed-in user or client-credentials service). The service that
    ///     opens the session derives it from the token; <see cref="RtPrincipalKind.Unknown" /> when it does not know.
    ///     Only types with a <c>writeAccess</c> block look at it, and they refuse <see cref="RtPrincipalKind.Unknown" />
    ///     (fail closed); unrestricted types ignore it.
    /// </summary>
    public RtPrincipalKind PrincipalKind { get; init; } = RtPrincipalKind.Unknown;

    /// <summary>
    ///     AB#8574: the trusted in-process write channel of the session, e.g. <c>System.Ai/KnowledgeTools</c>, matched
    ///     against the <c>channels</c> of a type's <c>writeAccess</c> block. Set only by service code that opens a session
    ///     for its own guarded write path (<see cref="WithWriteChannel" />); it must never be taken from a token, a header
    ///     or any other caller input. <c>null</c> for an ordinary session.
    /// </summary>
    public string? WriteChannel { get; init; }

    /// <summary>
    ///     Creates a context for an authenticated end user or client principal.
    /// </summary>
    /// <param name="subjectId">Subject id of the caller</param>
    /// <param name="roles">Role names from the caller's token</param>
    public static RtSecurityContext ForUser(string? subjectId, IEnumerable<string>? roles)
    {
        return new RtSecurityContext { SubjectId = subjectId, Roles = roles?.ToArray() ?? [] };
    }

    /// <summary>
    ///     Creates a context for an authenticated end user or client principal whose kind is known (AB#8574).
    /// </summary>
    /// <param name="subjectId">Subject id of the caller</param>
    /// <param name="roles">Role names from the caller's token</param>
    /// <param name="principalKind">Whether the caller is a signed-in user or a client-credentials service</param>
    public static RtSecurityContext ForUser(string? subjectId, IEnumerable<string>? roles,
        RtPrincipalKind principalKind)
    {
        return ForUser(subjectId, roles) with { PrincipalKind = principalKind };
    }

    /// <summary>
    ///     Returns a copy of this context that writes on the trusted in-process channel <paramref name="writeChannel" />
    ///     (AB#8574). Call it only from service code that opens the session for its own guarded write path; never with a
    ///     value from caller input.
    /// </summary>
    /// <param name="writeChannel">The channel, <c>&lt;namespace&gt;/&lt;name&gt;</c></param>
    public RtSecurityContext WithWriteChannel(string writeChannel)
    {
        if (string.IsNullOrWhiteSpace(writeChannel))
        {
            throw new ArgumentException("A write channel must not be empty.", nameof(writeChannel));
        }

        return this with { WriteChannel = writeChannel };
    }
}

/// <summary>
///     AB#8574: the kind of principal a session acts for (<see cref="RtSecurityContext.PrincipalKind" />).
/// </summary>
public enum RtPrincipalKind
{
    /// <summary>
    ///     Not known to the service that opened the session. Refused by types with a <c>writeAccess</c> block.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     A human user signed in interactively (CK principal kind <c>Interactive</c>).
    /// </summary>
    Interactive = 1,

    /// <summary>
    ///     A service principal authenticated with client credentials (CK principal kind <c>Client</c>).
    /// </summary>
    Client = 2
}
