namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     AB#6719: the GitHub API limit is exhausted and resets later than a build can sensibly wait. Thrown at once, with the
///     reset time, instead of letting a build sit in the retry loop until a task timeout fires with a text that does not
///     name the cause. No token or user name is part of the message.
/// </summary>
public sealed class GitHubRateLimitExhaustedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="resetAt">When the limit resets.</param>
    /// <param name="innerException">The Octokit exception that reported the limit.</param>
    public GitHubRateLimitExhaustedException(DateTimeOffset resetAt, Exception? innerException = null)
        : base($"GitHub rate limit exhausted, resets at {resetAt.UtcDateTime:HH:mm}Z.", innerException)
    {
        ResetAt = resetAt;
    }

    /// <summary>When the limit resets.</summary>
    public DateTimeOffset ResetAt { get; }
}
