namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a statistics value for a grouping.
/// </summary>
public class StatisticsResult
{
    /// <summary>
    ///     Attribute name of the calculated statistics.
    /// </summary>
    public string AttributeName { get; set; } = null!;

    /// <summary>
    ///     Value of the calculated statistics.
    /// </summary>
    public object? Value { get; set; }
}