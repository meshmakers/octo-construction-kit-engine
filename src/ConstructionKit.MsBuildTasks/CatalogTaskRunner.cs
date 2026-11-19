using Meshmakers.Octo.ConstructionKit.Contracts;
using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
///     Runs the asynchronous work of a CK MSBuild task under the task's time limit (AB#9388).
/// </summary>
/// <remarks>
///     The work gets a token that is canceled when the time limit is reached or MSBuild cancels the build
///     (<c>ICancelableTask.Cancel</c>, e.g. Ctrl+C), so a hanging catalog request ends with the task instead of running on
///     in the MSBuild node. Code that does not observe the token is still bounded: after the cancellation the runner waits
///     at most <c>grace</c> before it gives up on the work.
/// </remarks>
internal static class CatalogTaskRunner
{
    /// <summary>The time limit of CkCompile and CkRestore.</summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long the runner waits after the cancellation (time limit or MSBuild cancel) for the work to end.</summary>
    internal static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(30);

    internal enum Outcome
    {
        /// <summary>The work ran to completion.</summary>
        Completed,

        /// <summary>The time limit was reached; the work was canceled and ended.</summary>
        TimedOut,

        /// <summary>MSBuild canceled the build; the work was canceled and ended.</summary>
        Canceled,

        /// <summary>The work did not end within the grace period after its cancellation.</summary>
        Hung
    }

    /// <summary>
    ///     Runs <paramref name="work" /> and waits for it. Exceptions of the work other than its cancellation are thrown
    ///     as <see cref="AggregateException" />, as <see cref="Task.Wait()" /> does.
    /// </summary>
    internal static Outcome Run(Func<CancellationToken, Task> work, TimeSpan timeout, TimeSpan grace,
        CancellationToken buildCancellation)
    {
        using var timeoutSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, buildCancellation);
        timeoutSource.CancelAfter(timeout);
        var token = linkedSource.Token;

        var task = Task.Run(() => work(token), CancellationToken.None);
        try
        {
            // Wait for the work or its cancellation (time limit or MSBuild cancel), whichever comes first; after the
            // cancellation the work gets only the grace period to end.
            WaitHandle.WaitAny([((IAsyncResult)task).AsyncWaitHandle, token.WaitHandle]);
            if (!task.Wait(task.IsCompleted ? TimeSpan.Zero : grace))
            {
                return Outcome.Hung;
            }
        }
        catch (AggregateException ae) when (token.IsCancellationRequested && IsCancellation(ae))
        {
            return buildCancellation.IsCancellationRequested ? Outcome.Canceled : Outcome.TimedOut;
        }

        // Work that swallowed the cancellation and returned normally still exceeded the limit.
        if (timeoutSource.IsCancellationRequested)
        {
            return Outcome.TimedOut;
        }

        return buildCancellation.IsCancellationRequested ? Outcome.Canceled : Outcome.Completed;
    }

    /// <summary>
    ///     Logs why the work of a CK task did not complete. False when the task has to fail.
    /// </summary>
    internal static bool Report(TaskLoggingHelper log, Outcome outcome, string operation, TimeSpan timeout)
    {
        switch (outcome)
        {
            case Outcome.Completed:
                return true;
            case Outcome.Canceled:
                log.LogError("Construction kit {0} was canceled.", operation);
                return false;
            case Outcome.TimedOut:
                log.LogError(
                    "Construction kit {0} did not complete within {1} minutes and was canceled. This usually means a " +
                    "remote catalog (PublicGitHub/PrivateGitHub) is slow or does not answer. Verify network " +
                    "connectivity to GitHub and that the catalog API tokens are not rate-limited.",
                    operation, timeout.TotalMinutes);
                return false;
            default:
                log.LogError(
                    "Construction kit {0} did not complete within {1} minutes and did not end after its " +
                    "cancellation. This usually means a remote catalog (PublicGitHub/PrivateGitHub) call is hanging. " +
                    "Verify network connectivity to GitHub and that the catalog API tokens are not rate-limited.",
                    operation, timeout.TotalMinutes);
                return false;
        }
    }

    /// <summary>
    ///     The catalog request timeout in the chain of <paramref name="exception" />, if any: a catalog that accepted the
    ///     connection and did not answer.
    /// </summary>
    internal static ModelCatalogException? FindCatalogRequestTimeout(Exception exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is ModelCatalogException { IsRequestTimeout: true } timeout)
            {
                return timeout;
            }

            if (e is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindCatalogRequestTimeout(inner);
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            }
        }

        return null;
    }

    private static bool IsCancellation(AggregateException ae) =>
        ae.Flatten().InnerExceptions.All(e => e is OperationCanceledException);
}
