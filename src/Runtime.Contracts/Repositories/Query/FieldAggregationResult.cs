// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the result of a field aggregation operation.
/// </summary>
public class FieldAggregationResult : AggregationResult
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="groupByAttributePaths">The attribute names that were used to group the result</param>
    /// <param name="keys">Values of the keys that were used to group the result</param>
    /// <param name="count">Count of items in the group</param>
    /// <param name="countStatistics">Count statistics for each attribute</param>
    /// <param name="minStatistics">Min statistics for each attribute</param>
    /// <param name="maxStatistics">Max statistics for each attribute</param>
    /// <param name="avgStatistics">Average value statistics for each attribute</param>
    /// <param name="sumStatistics">Sum value statistics for each attribute</param>
    public FieldAggregationResult(IEnumerable<string> groupByAttributePaths, IEnumerable<object?> keys, long count,
        IEnumerable<StatisticsResult> countStatistics,
        IEnumerable<StatisticsResult> minStatistics,
        IEnumerable<StatisticsResult> maxStatistics,
        IEnumerable<StatisticsResult> avgStatistics,
        IEnumerable<StatisticsResult> sumStatistics)
    : base(count, countStatistics, minStatistics, maxStatistics, avgStatistics, sumStatistics)
    {
        GroupByAttributePaths = groupByAttributePaths;
        Keys = keys;
    }

    /// <summary>
    ///     Attribute names that were used to group the result
    /// </summary>
    public IEnumerable<string> GroupByAttributePaths { get; }

    /// <summary>
    ///     Returns the values of the keys that were used to group the result
    /// </summary>
    public IEnumerable<object?> Keys { get; }
}