namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Result of a model search operation
/// </summary>
public class ModelSearchResult : ModelListResult
{
    /// <summary>
    /// Returns the search term that was used
    /// </summary>
    public required string SearchTerm { get; init; }
}