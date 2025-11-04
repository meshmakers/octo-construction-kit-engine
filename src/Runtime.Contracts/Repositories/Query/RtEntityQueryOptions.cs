using Meshmakers.Octo.Runtime.Contracts.Geospatial.Geometry;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents query options for runtime queries.
/// </summary>
public record RtEntityQueryOptions : FieldFilterCriteria
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    /// <param name="language">The language to use for text search. This text has to be the two-letter ISO language name.</param>
    private RtEntityQueryOptions(LogicalOperators logicalOperator = LogicalOperators.And, string language = "en")
     : base(logicalOperator)
    {
        Language = language;
    }

    /// <summary>
    ///     The language to use for text search. This text has to be
    ///     the two-letter ISO language name.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string Language { get; private set; }

    /// <summary>
    ///     Represents global filter settings for the query.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public GlobalRtEntityFilter? GlobalFilter { get; private set; }

    /// <summary>
    ///     Represents full text search function for configured attributes (dependent of data source type)
    /// </summary>
    public TextSearchFilter? TextSearchFilter { get; private set; }

    /// <summary>
    ///     Represents text search function for specific attributes.
    /// </summary>
    public AttributeSearchFilter? AttributeSearchFilter { get; private set; }

    /// <summary>
    ///     Represents aggregation input for grouping by specific attributes.
    /// </summary>
    public FieldAggregationInput? FieldAggregation { get; private set; }

    /// <summary>
    /// Represents aggregation input for the result.
    /// </summary>
    public AggregationInput? ResultAggregation { get; private set; }

    /// <summary>
    ///     Represents sort order for specific attributes.
    /// </summary>
    public ICollection<SortOrderItem>? SortOrders { get; private set; }
    
    /// <summary>
    ///     Represents geospatial filter for specific attributes.
    /// </summary>
    public ICollection<GeospatialFilter>? GeospatialFilters { get; internal set; }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityQueryOptions" />.
    /// </summary>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    /// <param name="language">The language to use for text search. This text has to be the two-letter ISO language name.</param>
    /// <returns></returns>
    public static RtEntityQueryOptions Create(LogicalOperators logicalOperator = LogicalOperators.And, string language = "en")
    {
        return new RtEntityQueryOptions(logicalOperator, language);
    }

    /// <summary>
    ///     Uses the given language for text search.
    /// </summary>
    /// <param name="language">The language to use for text search. This text has to be the two letter ISO language name.</param>
    /// <returns></returns>
    public RtEntityQueryOptions UseLanguage(string language)
    {
        Language = language;
        return this;
    }

    /// <summary>
    ///     Adds a sort order to the query.
    /// </summary>
    /// <param name="attributePath">Path of attribute to sort</param>
    /// <param name="sortOrder">Sort order</param>
    public RtEntityQueryOptions SortOrder(string attributePath, SortOrders sortOrder)
    {
        SortOrders ??= new List<SortOrderItem>();

        SortOrders.Add(new SortOrderItem(attributePath, sortOrder));

        return this;
    }

    /// <summary>
    /// Defines global filter settings for runtime entity queries
    /// </summary>
    /// <param name="includeArchived">When true, archived entities are returned by the data operation, otherwise not</param>
    public RtEntityQueryOptions Global(bool includeArchived)
    {
        GlobalFilter = new GlobalRtEntityFilter(includeArchived);

        return this;
    }

    /// <summary>
    ///     Sets the text search filter.
    /// </summary>
    /// <param name="searchTerm">Search term for full text search.</param>
    public RtEntityQueryOptions TextSearch(object searchTerm)
    {
        TextSearchFilter = new TextSearchFilter(searchTerm);

        return this;
    }

    /// <summary>
    ///     Sets the attribute search filter.
    /// </summary>
    /// <param name="attributePaths">List of attribute paths for full text search</param>
    /// <param name="searchTerm">Search term for full text search.</param>
    public RtEntityQueryOptions AttributeSearch(IEnumerable<string> attributePaths, object searchTerm)
    {
        AttributeSearchFilter = new AttributeSearchFilter(attributePaths, searchTerm);

        return this;
    }

    /// <summary>
    ///     Groups by the given attribute paths.
    /// </summary>
    /// <param name="attributePaths">Attribute paths to group by.</param>
    /// <returns></returns>
    public FieldAggregationInput AggregateFieldGroupBy(params string[] attributePaths)
    {
        FieldAggregation = new FieldAggregationInput(attributePaths);

        return FieldAggregation;
    }

    /// <summary>
    ///     Aggregates the result
    /// </summary>
    /// <returns></returns>
    public AggregationInput AggregateResult()
    {
        ResultAggregation = new AggregationInput();

        return ResultAggregation;
    }
    
    /// <summary>
    ///     Adds a field filter to the query.
    /// </summary>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="point">Point to search for</param>
    /// <param name="minDistance">The minimum distance from the center point that the documents can be.</param>
    /// <param name="maxDistance">The maximum distance from the center point that the documents can be.</param>
    public RtEntityQueryOptions NearGeospatialFilter(string attributeName, Point point, double? minDistance, double? maxDistance)
    {
        GeospatialFilters ??= new List<GeospatialFilter>();

        GeospatialFilters.Add(new NearGeospatialFilter(attributeName,point, minDistance, maxDistance));

        return this;
    }
}