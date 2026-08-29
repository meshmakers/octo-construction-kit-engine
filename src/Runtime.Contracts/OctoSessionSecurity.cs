namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     A session that carries the identity of the caller it acts for. Implemented by session types that
///     support caller-scoped operation (RtCreatedBy stamping, data-level permissions — AB#4969); plain
///     <see cref="IOctoSession" /> implementations act as the system context.
/// </summary>
public interface IOctoSecureSession : IOctoSession
{
    /// <summary>
    ///     Identity of the caller this session acts for.
    /// </summary>
    RtSecurityContext SecurityContext { get; }
}

/// <summary>
///     Factory for caller-scoped sessions. Implemented next to the parameterless session factories;
///     resolved via the <c>GetSession(RtSecurityContext)</c> extension methods so existing factory
///     contracts stay unchanged.
/// </summary>
public interface ISecureSessionFactory
{
    /// <summary>
    ///     Gets a new session acting for the given caller.
    /// </summary>
    /// <param name="securityContext">Identity of the caller the session acts for</param>
    IOctoSession GetSession(RtSecurityContext securityContext);

    /// <summary>
    ///     Gets a new session acting for the given caller.
    /// </summary>
    /// <param name="securityContext">Identity of the caller the session acts for</param>
    Task<IOctoSession> GetSessionAsync(RtSecurityContext securityContext);
}

/// <summary>
///     Extension methods to read the security context of any session.
/// </summary>
public static class OctoSessionSecurityExtensions
{
    /// <summary>
    ///     Returns the security context the session acts for; sessions without one act as the system context.
    /// </summary>
    /// <param name="session">The session</param>
    public static RtSecurityContext GetSecurityContext(this IOctoSession session)
    {
        return session is IOctoSecureSession secureSession ? secureSession.SecurityContext : RtSecurityContext.System;
    }
}
