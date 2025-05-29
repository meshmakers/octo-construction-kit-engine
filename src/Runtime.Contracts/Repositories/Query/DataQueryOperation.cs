using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a data query operation.
/// </summary>
public record DataQueryOperation : FieldFilterCriteria
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="language">The language to use for text search. This text has to be the two letter ISO language name.</param>
    private DataQueryOperation(string language = "en")
    {
        Language = language;
    }

    /// <summary>
    ///     The language to use for text search. This text has to be
    ///     the two-letter ISO language name.
    /// </summary>
    public string Language { get; private set; }

    /// <summary>
    ///     Represents full text search function for configured attributes (dependent of data source type)
    /// </summary>
    public TextSearchFilter? TextSearchFilter { get; private set; }

    /// <summary>
    ///     Represents text search function for specific attributes.
    /// </summary>
    public AttributeSearchFilter? AttributeSearchFilter { get; private set; }

    /// <summary>
    ///     Represents field group by for specific attributes.
    /// </summary>
    public FieldGroupBy? FieldGroupBy { get; private set; }

    /// <summary>
    ///     Represents sort order for specific attributes.
    /// </summary>
    public ICollection<SortOrderItem>? SortOrders { get; private set; }
    
    /// <summary>
    ///     Represents geospatial filter for specific attributes.
    /// </summary>
    public ICollection<GeospatialFilter>? GeospatialFilters { get; internal set; }

    /// <summary>
    ///     Creates a new instance of <see cref="DataQueryOperation" />.
    /// </summary>
    /// <param name="language">The language to use for text search. This text has to be the two letter ISO language name.</param>
    /// <returns></returns>
    public static DataQueryOperation Create(string language = "en")
    {
        return new DataQueryOperation(language);
    }

    /// <summary>
    ///     Uses the given language for text search.
    /// </summary>
    /// <param name="language">The language to use for text search. This text has to be the two letter ISO language name.</param>
    /// <returns></returns>
    public DataQueryOperation UseLanguage(string language)
    {
        Language = language;
        return this;
    }

    /// <summary>
    ///     Adds a sort order to the query.
    /// </summary>
    /// <param name="attributeName">Attribute name</param>
    /// <param name="sortOrder">Sort order</param>
    public DataQueryOperation SortOrder(string attributeName, SortOrders sortOrder)
    {
        SortOrders ??= new List<SortOrderItem>();

        SortOrders.Add(new SortOrderItem(attributeName, sortOrder));

        return this;
    }

    /// <summary>
    ///     Sets the text search filter.
    /// </summary>
    /// <param name="searchTerm">Search term for full text search.</param>
    public DataQueryOperation TextSearch(object searchTerm)
    {
        TextSearchFilter = new TextSearchFilter(searchTerm);

        return this;
    }

    /// <summary>
    ///     Sets the attribute search filter.
    /// </summary>
    /// <param name="attributePaths">List of attribute paths for full text search</param>
    /// <param name="searchTerm">Search term for full text search.</param>
    public DataQueryOperation AttributeSearch(IEnumerable<string> attributePaths, object searchTerm)
    {
        AttributeSearchFilter = new AttributeSearchFilter(attributePaths, searchTerm);

        return this;
    }

    /// <summary>
    ///     Groups by the given attribute names.
    /// </summary>
    /// <param name="attributeNames">Attribute names to group by.</param>
    /// <returns></returns>
    public FieldGroupBy GroupBy(params string[] attributeNames)
    {
        FieldGroupBy = new FieldGroupBy(attributeNames);

        return FieldGroupBy;
    }
    
    /// <summary>
    ///     Adds a field filter to the query.
    /// </summary>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="point">Point to search for</param>
    /// <param name="minDistance">The minimum distance from the center point that the documents can be.</param>
    /// <param name="maxDistance">The maximum distance from the center point that the documents can be.</param>
    public DataQueryOperation NearGeospatialFilter(string attributeName, Point point, double? minDistance, double? maxDistance)
    {
        GeospatialFilters ??= new List<GeospatialFilter>();

        GeospatialFilters.Add(new NearGeospatialFilter(attributeName,point, minDistance, maxDistance));

        return this;
    }
}