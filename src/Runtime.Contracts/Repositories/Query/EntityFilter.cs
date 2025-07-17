// ReSharper disable MemberCanBePrivate.Global
namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a logical operator for combining filters
/// </summary>
public enum LogicalOperator
{
    /// <summary>
    ///     Logical AND operator
    /// </summary>
    And = 0,

    /// <summary>
    ///     Logical OR operator
    /// </summary>
    Or = 1
}

/// <summary>
///     Entity filter for complex filtering with logical operators
/// </summary>
public class EntityFilter
{
    /// <summary>
    ///     Creates a new instance of <see cref="EntityFilter" />
    /// </summary>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    public EntityFilter(LogicalOperator logicalOperator = LogicalOperator.And)
    {
        Operator = logicalOperator;
        Fields = new List<FieldFilter>();
        NestedFilters = new List<EntityFilter>();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityFilter" /> with field filters
    /// </summary>
    /// <param name="fieldFilters">The field filters to apply</param>
    /// <param name="logicalOperator">The logical operator to use for combining field filters</param>
    public EntityFilter(IEnumerable<FieldFilter> fieldFilters, LogicalOperator logicalOperator = LogicalOperator.And)
    {
        if (fieldFilters == null)
            throw new ArgumentNullException(nameof(fieldFilters));

        Operator = logicalOperator;
        Fields = new List<FieldFilter>(fieldFilters);
        NestedFilters = new List<EntityFilter>();
    }

    /// <summary>
    ///     Gets the logical operator for combining field filters
    /// </summary>
    public LogicalOperator Operator { get; }

    /// <summary>
    ///     Gets the list of field filters
    /// </summary>
    public List<FieldFilter> Fields { get; }

    /// <summary>
    ///     Gets the list of nested filters for complex logical operations
    /// </summary>
    public List<EntityFilter> NestedFilters { get; }

    /// <summary>
    ///     Adds a field filter to the entity filter
    /// </summary>
    /// <param name="fieldFilter">The field filter to add</param>
    public void AddField(FieldFilter fieldFilter)
    {
        if (fieldFilter == null)
            throw new ArgumentNullException(nameof(fieldFilter));
        Fields.Add(fieldFilter);
    }

    /// <summary>
    ///     Adds multiple field filters to the entity filter
    /// </summary>
    /// <param name="fieldFilters">The field filters to add</param>
    public void AddFields(IEnumerable<FieldFilter> fieldFilters)
    {
        if (fieldFilters == null)
            throw new ArgumentNullException(nameof(fieldFilters));
        Fields.AddRange(fieldFilters);
    }

    /// <summary>
    ///     Adds a nested filter to the entity filter
    /// </summary>
    /// <param name="nestedFilter">The nested filter to add</param>
    public void AddNestedFilter(EntityFilter nestedFilter)
    {
        if (nestedFilter == null)
            throw new ArgumentNullException(nameof(nestedFilter));
        NestedFilters.Add(nestedFilter);
    }

    /// <summary>
    ///     Adds multiple nested filters to the entity filter
    /// </summary>
    /// <param name="nestedFilters">The nested filters to add</param>
    public void AddNestedFilters(IEnumerable<EntityFilter> nestedFilters)
    {
        if (nestedFilters == null)
            throw new ArgumentNullException(nameof(nestedFilters));
        NestedFilters.AddRange(nestedFilters);
    }

    /// <summary>
    ///     Checks if the entity filter has any conditions
    /// </summary>
    /// <returns>True if the filter has any field filters or nested filters</returns>
    public bool HasConditions()
    {
        return Fields.Any() || NestedFilters.Any();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new List<string>();

        if (Fields.Any())
        {
            parts.AddRange(Fields.Select(f => f.ToString()));
        }

        if (NestedFilters.Any())
        {
            parts.AddRange(NestedFilters.Select(nf => $"({nf})"));
        }

        return string.Join($" {Operator} ", parts);
    }
}