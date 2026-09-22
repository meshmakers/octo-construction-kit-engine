using System.Net;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// AB#5298 - the retry classification of <see cref="GitHubClientWrapper" />. The wrapper talks to
/// Octokit directly, so what can be unit-tested is the policy: which answers are retried, which
/// are not, and how long the wait grows. The r1.0.89 release died on a single untyped
/// <see cref="ApiException" /> during the public-catalog publish, minutes after the CK-model
/// publish of the same build had written dozens of commits to the same repository.
/// </summary>
public class GitHubClientWrapperRetryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void ServerErrors_AreTransient(HttpStatusCode status)
    {
        var ex = new ApiException("An error occurred with this API request", status);

        Assert.True(GitHubClientWrapper.IsTransient(ex));
    }

    [Fact]
    public void Forbidden_NamingARateLimit_IsTransient()
    {
        var ex = new ApiException("You have exceeded a secondary rate limit. Please wait a few minutes before you try again.",
            HttpStatusCode.Forbidden);

        Assert.True(GitHubClientWrapper.IsTransient(ex));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Resource not accessible by integration")]
    [InlineData(HttpStatusCode.NotFound, "Not Found")]
    [InlineData(HttpStatusCode.Unauthorized, "Bad credentials")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Validation Failed")]
    public void ClientErrors_ThatAreNotRateLimits_AreNotTransient(HttpStatusCode status, string message)
    {
        var ex = new ApiException(message, status);

        Assert.False(GitHubClientWrapper.IsTransient(ex));
    }

    [Fact]
    public void ShaConflict_IsNotTransient_ItHasItsOwnRecovery()
    {
        // A 409 is handled by the SHA-conflict path (re-read, retry with the fresh sha); the
        // transient path must not shadow it with a blind resend of the stale request.
        var ex = new ApiException("Conflict", HttpStatusCode.Conflict);

        Assert.False(GitHubClientWrapper.IsTransient(ex));
    }

    [Fact]
    public void RetryDelay_GrowsExponentially_AndIsCapped()
    {
        var ex = new ApiException("boom", HttpStatusCode.BadGateway);

        var first = GitHubClientWrapper.RetryDelay(ex, 0);
        var second = GitHubClientWrapper.RetryDelay(ex, 1);
        var third = GitHubClientWrapper.RetryDelay(ex, 2);
        var late = GitHubClientWrapper.RetryDelay(ex, 20);

        Assert.True(second > first);
        Assert.True(third > second);
        Assert.Equal(TimeSpan.FromSeconds(30), late);
    }
}
