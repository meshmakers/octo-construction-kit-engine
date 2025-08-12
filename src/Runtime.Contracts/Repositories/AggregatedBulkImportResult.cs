namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Represents the result of a bulk import operation, aggregating results from multiple bulk import operations.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class AggregatedBulkImportResult
{
    private readonly IEnumerable<IBulkImportResult> _bulkWriteResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregatedBulkImportResult"/> class with the provided bulk import results.
    /// </summary>
    /// <param name="bulkWriteResult"></param>
    public AggregatedBulkImportResult(IEnumerable<IBulkImportResult> bulkWriteResult)
    {
        _bulkWriteResult = bulkWriteResult;
    }

    /// <summary>
    /// Gets count of documents that were inserted during the bulk import operation.
    /// </summary>
    public long InsertedCount => _bulkWriteResult.Sum(x => x.InsertedCount);

    /// <summary>
    /// Gets count of documents that were deleted during the bulk import operation.
    /// </summary>
    public long DeletedCount => _bulkWriteResult.Sum(x => x.DeletedCount);

    /// <summary>
    /// Gets count of documents that were modified during the bulk import operation.
    /// </summary>
    public long ModifiedCount => _bulkWriteResult.Sum(x => x.ModifiedCount);

    /// <summary>
    /// Helps to determine if any of the bulk import operations encountered an error.
    /// </summary>
    /// <returns></returns>
    public bool HasError()
    {
        return _bulkWriteResult.Any(x => x.HasError());
    }
}