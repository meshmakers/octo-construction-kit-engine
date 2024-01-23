namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a collection of field filters for a query.
/// </summary>
public class FieldFilterCriteria
{
    /// <summary>
    /// Creates a new instance of <see cref="FieldFilterCriteria" />
    /// </summary>
    protected FieldFilterCriteria()
    {
        
    }
    
    /// <summary>
    ///     Represents field filters for specific attributes with different comparison operators.
    /// </summary>
    public ICollection<FieldFilter>? FieldFilters { get; internal set; }
    
    /// <summary>
    ///     Adds a field filter to the query.
    /// </summary>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public void AddFieldFilter(string attributeName, FieldFilterOperator comparisonOperator, object? comparisonValue)
    {
        FieldFilters ??= new List<FieldFilter>();

        FieldFilters.Add(new FieldFilter(attributeName, comparisonOperator, comparisonValue));
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="FieldFilterCriteria" />.
    /// </summary>
    /// <returns></returns>
    public static FieldFilterCriteria Create()
    {
        return new FieldFilterCriteria();
    }
}