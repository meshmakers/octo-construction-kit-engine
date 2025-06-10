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
    public BulkInsertStrategy BulkInsertStrategy { get; init; } = BulkInsertStrategy.InsertOnly;

    /// <summary>
    /// Returns the default bulk runtime mutation options.
    /// </summary>
    public static BulkRtMutationOptions Default => new();
}