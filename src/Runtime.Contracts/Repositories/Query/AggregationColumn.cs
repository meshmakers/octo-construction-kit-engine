using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Specifies a single aggregation column: the attribute path to aggregate and the function to apply.
/// </summary>
public class AggregationColumn
{
    /// <summary>
    ///     Creates a new instance.
    /// </summary>
    public AggregationColumn(string attributePath, AggregationFunction function, string? comparisonValue = null)
    {
        ArgumentValidation.ValidateString(nameof(attributePath), attributePath);

        AttributePath = attributePath;
        Function = function;
        ComparisonValue = comparisonValue;
    }

    /// <summary>
    ///     Path to the attribute to aggregate.
    /// </summary>
    public string AttributePath { get; }

    /// <summary>
    ///     Aggregation function to apply.
    /// </summary>
    public AggregationFunction Function { get; }

    /// <summary>
    ///     State literal a <see cref="AggregationFunction.StateDuration"/> column matches the
    ///     attribute against — a number (<c>"2"</c>), a boolean (<c>"true"</c>/<c>"false"</c>) or a
    ///     string state name. Required for StateDuration; ignored otherwise. AB#4336.
    /// </summary>
    public string? ComparisonValue { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Function}({AttributePath})";
}
