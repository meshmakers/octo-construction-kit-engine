namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Extension methods to work fluently with data query operations
/// </summary>
public static class DataQueryOperationExtensions
{
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute contains the given substring.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Substring to search for</param>
    public static DataQueryOperation FieldContains(this DataQueryOperation @this, string attributeName, string? comparisonValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.Contains, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute starts with the given prefix.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Prefix to search for</param>
    public static DataQueryOperation FieldStartsWith(this DataQueryOperation @this, string attributeName, string? comparisonValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.StartsWith, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute ends with the given suffix.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Suffix to search for</param>
    public static DataQueryOperation FieldEndsWith(this DataQueryOperation @this, string attributeName, string? comparisonValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.EndsWith, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater than or equal to the given value.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static DataQueryOperation FieldGreaterThanOrEqual(this DataQueryOperation @this, string attributeName, object? comparisonValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.GreaterEqualThan, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less than or equal to the given value.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static DataQueryOperation FieldLessThanOrEqual(this DataQueryOperation @this, string attributeName, object? comparisonValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.LessEqualThan, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is between two values (inclusive).
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="lowerValue">Lower bound value</param>
    /// <param name="upperValue">Upper bound value</param>
    public static DataQueryOperation FieldBetween(this DataQueryOperation @this, string attributeName, object? lowerValue, object? upperValue)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.Between, lowerValue, upperValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is null.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    public static DataQueryOperation FieldIsNull(this DataQueryOperation @this, string attributeName)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.IsNull, null);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not null.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    public static DataQueryOperation FieldIsNotNull(this DataQueryOperation @this, string attributeName)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.IsNotNull, null);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute matches the given regular expression.
    /// </summary>
    /// <param name="this">The data query operation</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="pattern">Regular expression pattern</param>
    public static DataQueryOperation FieldRegex(this DataQueryOperation @this, string attributeName, string? pattern)
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.MatchRegEx, pattern);
        return @this;
    }
}