namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Represents a session.
/// </summary>
public interface IOctoSession : IDisposable
{
    /// <summary>
    /// Starts a transaction.
    /// </summary>
    void StartTransaction();

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <returns></returns>
    Task CommitTransactionAsync();

    /// <summary>
    /// Aborts the transaction.
    /// </summary>
    /// <returns></returns>
    Task AbortTransactionAsync();
}
