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
    ///     Creates a context for an authenticated end user or client principal.
    /// </summary>
    /// <param name="subjectId">Subject id of the caller</param>
    /// <param name="roles">Role names from the caller's token</param>
    public static RtSecurityContext ForUser(string? subjectId, IEnumerable<string>? roles)
    {
        return new RtSecurityContext { SubjectId = subjectId, Roles = roles?.ToArray() ?? [] };
    }
}
