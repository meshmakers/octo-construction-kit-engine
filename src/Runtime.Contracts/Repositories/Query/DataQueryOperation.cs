namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a data query operation.
/// </summary>
public class DataQueryOperation
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="language">The language to use for text search. This text has to be the two letter ISO language name.</param>
    public DataQueryOperation(string language = "en")
    {
        Language = language;
    }

    /// <summary>
    /// The language to use for text search. This text has to be
    ///  the two letter ISO language name.
    /// </summary>
    public string Language { get; private set; }

    /// <summary>
    /// Represents full text search function for configured attributes (dependent of data source type)
    /// </summary>
    public TextSearchFilter? TextSearchFilter { get; private set; }

    /// <summary>
    /// Represents text search function for specific attributes.
    /// </summary>
    public AttributeSearchFilter? AttributeSearchFilter { get; private set; }

    /// <summary>
    /// Represents field filters for specific attributes with different comparison operators.
    /// </summary>
    public ICollection<FieldFilter>? FieldFilters { get; private set; }

    /// <summary>
    /// Represents sort order for specific attributes.
    /// </summary>
    public ICollection<SortOrderItem>? SortOrders { get; private set; }
    
    /// <summary>
    /// Creates a new instance of <see cref="DataQueryOperation"/>.
    /// </summary>
    /// <param name="language"></param>
    /// <returns></returns>
    public static DataQueryOperation Create(string language = "en")
    {
        return new DataQueryOperation(language);
    }

    /// <summary>
    /// Adds a field filter to the query.
    /// </summary>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public DataQueryOperation FieldFilter(string attributeName, FieldFilterOperator comparisonOperator, object? comparisonValue)
    {
        FieldFilters ??= new List<FieldFilter>();

        FieldFilters.Add(new FieldFilter(attributeName, comparisonOperator, comparisonValue));
        
        return this;
    }
    
    /// <summary>
    /// Adds a sort order to the query.
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
    /// Sets the text search filter.
    /// </summary>
    /// <param name="searchTerm">Search term for full text search.</param>
    public DataQueryOperation TextSearch(object searchTerm)
    {
        TextSearchFilter = new TextSearchFilter(searchTerm);

        return this;
    }
    
    /// <summary>
    /// Sets the attribute search filter.
    /// </summary>
    /// <param name="attributeNames">List of attribute names for full text search</param>
    /// <param name="searchTerm">Search term for full text search.</param>
    public DataQueryOperation AttributeSearch(IEnumerable<string> attributeNames, object searchTerm)
    {
        AttributeSearchFilter = new AttributeSearchFilter(attributeNames, searchTerm);
        
        return this;
    }
}