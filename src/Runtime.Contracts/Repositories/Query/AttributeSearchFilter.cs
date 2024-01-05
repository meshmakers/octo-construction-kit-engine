namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a text search filter for specific attributes.
/// </summary>
public class AttributeSearchFilter
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="attributeNames">List of attribute names</param>
    /// <param name="searchTerm">Search term that must exist in one of the given attributes</param>
    public AttributeSearchFilter(IEnumerable<string> attributeNames, object searchTerm)
    {
        AttributeNames = attributeNames;
        SearchTerm = searchTerm;
    }

    /// <summary>
    ///     Search term that must exist in one of the given attributes
    /// </summary>
    public object SearchTerm { get; }

    /// <summary>
    ///     Attribute names to search in
    /// </summary>
    public IEnumerable<string> AttributeNames { get; }
}