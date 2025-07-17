namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a collection of field filters for a query.
/// </summary>
public record FieldFilterCriteria
{
    /// <summary>
    /// Creates a new instance of <see cref="FieldFilterCriteria" />
    /// </summary>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    protected FieldFilterCriteria(LogicalOperator logicalOperator = LogicalOperator.And)
    {
        Operator = logicalOperator;
    }

    /// <summary>
    ///     Gets the logical operator for combining field filters
    /// </summary>
    public LogicalOperator Operator { get; }
    
    /// <summary>
    ///     Represents field filters for specific attributes with different comparison operators.
    /// </summary>
    public ICollection<FieldFilter>? FieldFilters { get; private set; }

    /// <summary>
    ///     Gets the list of nested filters for complex logical operations
    /// </summary>
    public List<FieldFilterCriteria>? NestedFilters { get; private set; }
    
    /// <summary>
    ///     Adds a field filter to the query.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public void AddFieldFilter(string attributePath, FieldFilterOperator comparisonOperator, object? comparisonValue)
    {
        FieldFilters ??= new List<FieldFilter>();

        FieldFilters.Add(new FieldFilter(attributePath, comparisonOperator, comparisonValue));
    }

    /// <summary>
    ///     Adds a field filter to the query with a secondary value.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Primary comparison value of the field filter</param>
    /// <param name="secondaryValue">Secondary comparison value (used for operators like Between)</param>
    public void AddFieldFilter(string attributePath, FieldFilterOperator comparisonOperator, object? comparisonValue, object? secondaryValue)
    {
        FieldFilters ??= new List<FieldFilter>();

        FieldFilters.Add(new FieldFilter(attributePath, comparisonOperator, comparisonValue, secondaryValue));
    }

    /// <summary>
    ///     Adds a nested filter to the entity filter
    /// </summary>
    /// <param name="nestedFilter">The nested filter to add</param>
    public void AddNestedFilter(FieldFilterCriteria nestedFilter)
    {
        if (nestedFilter == null)
        {
            throw new ArgumentNullException(nameof(nestedFilter));
        }

        NestedFilters ??= new List<FieldFilterCriteria>();
        NestedFilters.Add(nestedFilter);
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="FieldFilterCriteria" /> using the default logical operator (And).
    /// </summary>
    /// <returns>New instance of <see cref="FieldFilterCriteria" /></returns>
    public static FieldFilterCriteria Create()
    {
        return new FieldFilterCriteria();
    }

    /// <summary>
    /// Creates a new instance of <see cref="FieldFilterCriteria" />.
    /// </summary>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    /// <returns>New instance of <see cref="FieldFilterCriteria" /></returns>
    public static FieldFilterCriteria Create(LogicalOperator logicalOperator)
    {
        return new FieldFilterCriteria(logicalOperator);
    }
}