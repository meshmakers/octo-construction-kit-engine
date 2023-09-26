namespace Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

public interface IOctoSession : IDisposable
{
    void StartTransaction();

    Task CommitTransactionAsync();

    Task AbortTransactionAsync();
}
