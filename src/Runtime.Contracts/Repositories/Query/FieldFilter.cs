using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Field filter object for use in queries
/// </summary>
public class FieldFilter
{
    /// <summary>
    ///     Creates a new instance of <see cref="FieldFilter" />
    /// </summary>
    /// <param name="attributePath">The path to the attribute to compare</param>
    /// <param name="comparisonOperator">Operator to use for the comparison</param>
    /// <param name="comparisonValue">The value to compare with</param>
    public FieldFilter(string attributePath, FieldFilterOperator comparisonOperator, object? comparisonValue)
    {
        ArgumentValidation.ValidateString(nameof(attributePath), attributePath);

        AttributePath = attributePath;
        Operator = comparisonOperator;
        ComparisonValue = comparisonValue;
    }

    /// <summary>
    ///     Gets the path to the attribute to compare
    /// </summary>
    public string AttributePath { get; }

    /// <summary>
    ///     The operator to use for the comparison
    /// </summary>
    public FieldFilterOperator Operator { get; }

    /// <summary>
    ///     The value to compare with
    /// </summary>
    public object? ComparisonValue { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{AttributePath} {Operator} {ComparisonValue}";
    }
}