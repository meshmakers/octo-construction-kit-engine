namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Local;

/// <summary>
/// Represents a local session
/// </summary>
public class LocalSession : IOctoSession
{
    /// <inheritdoc />
    public void Dispose()
    {
        // TODO release managed resources here
    }

    /// <inheritdoc />
    public void StartTransaction()
    {
    }

    /// <inheritdoc />
    public Task CommitTransactionAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbortTransactionAsync()
    {
        return Task.CompletedTask;
    }
}