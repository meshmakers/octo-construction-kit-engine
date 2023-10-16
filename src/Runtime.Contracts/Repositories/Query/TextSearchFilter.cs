namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a full text search filter.
/// </summary>
public class TextSearchFilter
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="searchTerm">Search term for full text search.</param>
    internal TextSearchFilter(object searchTerm)
    {
        SearchTerm = searchTerm;
    }

    /// <summary>
    /// Sets the search term.
    /// </summary>
    public object SearchTerm { get; }
}