namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a text search filter for specific attributes.
/// </summary>
public class AttributeSearchFilter
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="attributePaths">List of attribute paths</param>
    /// <param name="searchTerm">Search term that must exist in one of the given attributes</param>
    public AttributeSearchFilter(IEnumerable<string> attributePaths, object searchTerm)
    {
        AttributePaths = attributePaths;
        SearchTerm = searchTerm;
    }

    /// <summary>
    ///     Search term that must exist in one of the given attributes
    /// </summary>
    public object SearchTerm { get; }

    /// <summary>
    ///     Attribute paths to search in
    /// </summary>
    public IEnumerable<string> AttributePaths { get; }
}