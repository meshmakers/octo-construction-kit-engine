using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalBulkImportResult : IBulkImportResult
{
    private bool _hasError;

    public LocalBulkImportResult(long insertedCount, long deletedCount, long modifiedCount, bool hasError)
    {
        _hasError = hasError;
        InsertedCount = insertedCount;
        DeletedCount = deletedCount;
        ModifiedCount = modifiedCount;
    }

    public long InsertedCount { get; }
    public long DeletedCount { get; }
    public long ModifiedCount { get; }
    public bool HasError()
    {
        return _hasError;
    }
}