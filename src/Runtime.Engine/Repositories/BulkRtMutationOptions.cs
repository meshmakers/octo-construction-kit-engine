using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
/// Defines options for bulk runtime mutations.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record BulkRtMutationOptions
{
    /// <summary>
    /// Disable pre-document modifications when performing insert or replace operations.
    /// </summary>
    public bool DisablePreDocumentModifications { get; init; } = false;

    /// <summary>
    /// When set to true, the mutation will use a bulk insert/update/replace/delete strategy
    /// </summary>
    public bool UseBulkMode { get; init; } = false;

    /// <summary>
    /// Defines the strategy for bulk insert operations.
    /// </summary>
    public BulkInsertStrategies BulkInsertStrategy { get; init; } = BulkInsertStrategies.InsertOnly;

    /// <summary>
    /// Defines the strategy for bulk delete operations.
    /// </summary>
    public DeleteStrategies DeleteStrategy { get; init; } = DeleteStrategies.Archive;

    /// <summary>
    /// Gets delete options
    /// </summary>
    /// <returns></returns>
    public DeleteOptions ToDeleteOptions() => new DeleteOptions{ Strategy = DeleteStrategy };

    /// <summary>
    /// Returns the default bulk runtime mutation options.
    /// </summary>
    /// <returns>New options objects</returns>
    public static BulkRtMutationOptions Default => new();

    /// <summary>
    /// Creates a bulk mutation options object from a delete strategy
    /// </summary>
    /// <param name="deleteOptions">The delete options object.</param>
    /// <returns>New options objects</returns>
    public static BulkRtMutationOptions FromDeleteOptions(DeleteOptions deleteOptions) =>
        new() { DeleteStrategy = deleteOptions.Strategy };
}