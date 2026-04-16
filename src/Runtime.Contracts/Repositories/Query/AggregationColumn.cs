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
    public AggregationColumn(string attributePath, AggregationFunction function)
    {
        ArgumentValidation.ValidateString(nameof(attributePath), attributePath);

        AttributePath = attributePath;
        Function = function;
    }

    /// <summary>
    ///     Path to the attribute to aggregate.
    /// </summary>
    public string AttributePath { get; }

    /// <summary>
    ///     Aggregation function to apply.
    /// </summary>
    public AggregationFunction Function { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Function}({AttributePath})";
}
