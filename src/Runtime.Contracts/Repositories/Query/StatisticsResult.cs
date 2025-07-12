namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a statistics value for a grouping.
/// </summary>
public class StatisticsResult
{
    /// <summary>
    ///     Path of attribute the calculated statistics is for.
    /// </summary>
    public string AttributePath { get; set; } = null!;

    /// <summary>
    ///     Value of the calculated statistics.
    /// </summary>
    public object? Value { get; set; }
}