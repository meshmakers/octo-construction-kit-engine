namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Defines options for bulk operations.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record BulkOperationOptions
{
    /// <summary>
    /// Defines the strategy for bulk insert operations.
    /// </summary>
    public BulkInsertStrategies InsertStrategy { get; init; } = BulkInsertStrategies.InsertOnly;

    /// <summary>
    /// Defines the strategy for delete operations
    /// </summary>
    public DeleteStrategies DeleteStrategy  { get; init; } = DeleteStrategies.Archive;

    /// <summary>
    /// Returns the default options for bulk operations.
    /// </summary>
    public static BulkOperationOptions Default => new();
}