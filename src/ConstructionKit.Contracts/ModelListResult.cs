namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Result of a model list operation
/// </summary>
public class ModelListResult
{
    /// <summary>
    /// Returns the total count of models that match the defined filter criteria
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Amount of models that were skipped because of pagination
    /// </summary>
    public required int SkippedCount { get; init; }

    /// <summary>
    /// Amount of models that were taken because of pagination
    /// </summary>
    public required int TakeCount { get; init; }

    /// <summary>
    /// Returns the list of models
    /// </summary>
    public required IReadOnlyCollection<CatalogResultItem> ModelResultItems { get; init; }
}