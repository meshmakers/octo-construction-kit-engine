namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Extension methods to work fluently with field filter criteria
/// </summary>
public static class FieldFilterCriteriaExtensions
{
    /// <summary>
    ///     Adds a field filter to the query.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldFilter<T>(this T @this, string attributeName, FieldFilterOperator comparisonOperator, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, comparisonOperator, comparisonValue);
        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldEquals<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.Equals, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldNotEquals<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.NotEquals, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater than to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldGreaterThan<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.GreaterThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater or equals to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldGreaterEqualThan<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.GreaterEqualThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less than the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLessThan<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.LessThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less or equals to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLessEqualThan<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.LessEqualThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldIn<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.In, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldNotIn<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.NotIn, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is like in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLike<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.Like, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute matches the given regex expression.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldMatchRegex<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.MatchRegEx, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if any value of an array attribute equals.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldAnyEq<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.AnyEq, comparisonValue);

        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if any value of an array attribute is like the given value.
    /// </summary>
    /// <remarks>
    /// Wildcards are allowed in the comparison value.
    /// </remarks>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldAnyLike<T>(this T @this, string attributeName, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.AnyLike, comparisonValue);

        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute match. A new filter collection is returned to describe the match.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributeName">Name of attribute</param>
    /// <param name="fieldFilterCriteria">A sub selection of filter criteria</param>
    /// <returns>A new filter collection to describe the match</returns>
    public static T MatchField<T>(this T @this, string attributeName, FieldFilterCriteria fieldFilterCriteria)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributeName, FieldFilterOperator.Match, fieldFilterCriteria);

        return @this;
    }
}