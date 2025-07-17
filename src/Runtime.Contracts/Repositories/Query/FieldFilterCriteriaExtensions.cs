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
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonOperator">Operator of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldFilter<T>(this T @this, string attributePath, FieldFilterOperator comparisonOperator, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, comparisonOperator, comparisonValue);
        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldEquals<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.Equals, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldNotEquals<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.NotEquals, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater than to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldGreaterThan<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.GreaterThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater or equals to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldGreaterEqualThan<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.GreaterEqualThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less than the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLessThan<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.LessThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less or equals to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLessEqualThan<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.LessEqualThan, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldIn<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.In, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldNotIn<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.NotIn, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is like in the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLike<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.Like, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute matches the given regex expression.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldMatchRegex<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.MatchRegEx, comparisonValue);

        return @this;
    }
    
    /// <summary>
    ///     Adds a field filter that checks if any value of an array attribute equals.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldAnyEq<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.AnyEq, comparisonValue);

        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if any value of an array attribute is like the given value.
    /// </summary>
    /// <remarks>
    /// Wildcards are allowed in the comparison value.
    /// </remarks>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldAnyLike<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.AnyLike, comparisonValue);

        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute match. A new filter collection is returned to describe the match.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="fieldFilterCriteria">A sub selection of filter criteria</param>
    /// <returns>A new filter collection to describe the match</returns>
    public static T MatchField<T>(this T @this, string attributePath, FieldFilterCriteria fieldFilterCriteria)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.Match, fieldFilterCriteria);

        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute contains the given substring.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Substring to search for</param>
    public static T FieldContains<T>(this T @this, string attributePath, string comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.Contains, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute starts with the given prefix.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Prefix to search for</param>
    public static T FieldStartsWith<T>(this T @this, string attributePath, string comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.StartsWith, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute ends with the given suffix.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Suffix to search for</param>
    public static T FieldEndsWith<T>(this T @this, string attributePath, string comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.EndsWith, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is greater than or equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldGreaterThanOrEqual<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.GreaterEqualThan, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is less than or equal to the given value.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="comparisonValue">Comparison value of the field filter</param>
    public static T FieldLessThanOrEqual<T>(this T @this, string attributePath, object? comparisonValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.LessEqualThan, comparisonValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is between two values (inclusive).
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="lowerValue">Lower bound value</param>
    /// <param name="upperValue">Upper bound value</param>
    public static T FieldBetween<T>(this T @this, string attributePath, object? lowerValue, object? upperValue)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.Between, lowerValue, upperValue);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is null.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    public static T FieldIsNull<T>(this T @this, string attributePath)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.IsNull, null);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute is not null.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    public static T FieldIsNotNull<T>(this T @this, string attributePath)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.IsNotNull, null);
        return @this;
    }

    /// <summary>
    ///     Adds a field filter that checks if the value of an attribute matches the given regular expression.
    /// </summary>
    /// <param name="this">The field filter collection</param>
    /// <param name="attributePath">Path of attribute</param>
    /// <param name="pattern">Regular expression pattern</param>
    public static T FieldRegex<T>(this T @this, string attributePath, string pattern)
        where T : FieldFilterCriteria
    {
        @this.AddFieldFilter(attributePath, FieldFilterOperator.MatchRegEx, pattern);
        return @this;
    }
}