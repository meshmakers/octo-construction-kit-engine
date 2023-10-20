namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Returns the result of a bulk import operation
/// </summary>
public interface IBulkImportResult
{
    /// <summary>
    /// Number of documents inserted
    /// </summary>
    long InsertedCount { get; }
    
    /// <summary>
    /// Number of documents deleted
    /// </summary>
    long DeletedCount { get; }
    
    /// <summary>
    /// Number of documents modified
    /// </summary>
    long ModifiedCount { get; }
    
    /// <summary>
    /// Returns true if the operation failed
    /// </summary>
    /// <returns></returns>
    bool HasError();
}