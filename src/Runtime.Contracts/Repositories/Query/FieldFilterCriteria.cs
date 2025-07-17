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
    public  FieldFilterCriteria Create(LogicalOperator logicalOperator)
    {
        return new FieldFilterCriteria(logicalOperator);
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute contains the given substring.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Substring to search for</param>
    public FieldFilterCriteria FieldContains(string attributePath, string? comparisonValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.Contains, comparisonValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute starts with the given prefix.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Prefix to search for</param>
    public FieldFilterCriteria FieldStartsWith(string attributePath, string? comparisonValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.StartsWith, comparisonValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute ends with the given suffix.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Suffix to search for</param>
    public FieldFilterCriteria FieldEndsWith(string attributePath, string? comparisonValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.EndsWith, comparisonValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater than or equal to the given value.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public FieldFilterCriteria FieldGreaterThanOrEqual(string attributePath, object? comparisonValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.GreaterEqualThan, comparisonValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less than or equal to the given value.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public FieldFilterCriteria FieldLessThanOrEqual(string attributePath, object? comparisonValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.LessEqualThan, comparisonValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is between two values (inclusive).
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="lowerValue">Lower bound value</param>
    /// <param name="upperValue">Upper bound value</param>
    public FieldFilterCriteria FieldBetween(string attributePath, object? lowerValue, object? upperValue)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.Between, lowerValue, upperValue);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is null.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    public FieldFilterCriteria FieldIsNull(string attributePath)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.IsNull, null);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not null.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    public FieldFilterCriteria FieldIsNotNull(string attributePath)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.IsNotNull, null);
        return this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute matches the given regular expression.
    /// </summary>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="pattern">Regular expression pattern</param>
    public FieldFilterCriteria FieldRegex(string attributePath, string? pattern)
    {
        AddFieldFilter(attributePath, FieldFilterOperator.MatchRegEx, pattern);
        return this;
    }
}