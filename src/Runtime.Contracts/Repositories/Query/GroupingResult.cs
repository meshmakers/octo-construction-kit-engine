// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the result of a grouping
/// </summary>
public class GroupingResult
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="groupByAttributeNames">The attribute names that were used to group the result</param>
    /// <param name="keys">Values of the keys that were used to group the result</param>
    /// <param name="count">Count of items in the group</param>
    /// <param name="countStatistics">Count statistics for each attribute</param>
    /// <param name="minStatistics">Min statistics for each attribute</param>
    /// <param name="maxStatistics">Max statistics for each attribute</param>
    /// <param name="avgStatistics">Average value statistics for each attribute</param>
    public GroupingResult(IEnumerable<string> groupByAttributeNames, IEnumerable<object?> keys, long count,
        IEnumerable<StatisticsResult> countStatistics,
        IEnumerable<StatisticsResult> minStatistics,
        IEnumerable<StatisticsResult> maxStatistics,
        IEnumerable<StatisticsResult> avgStatistics)
    {
        GroupByAttributeNames = groupByAttributeNames;
        Keys = keys;
        Count = count;
        CountStatistics = countStatistics;
        MinStatistics = minStatistics;
        MaxStatistics = maxStatistics;
        AvgStatistics = avgStatistics;
    }

    /// <summary>
    ///     Attribute names that were used to group the result
    /// </summary>
    public IEnumerable<string> GroupByAttributeNames { get; }

    /// <summary>
    ///     Returns the values of the keys that were used to group the result
    /// </summary>
    public IEnumerable<object?> Keys { get; }

    /// <summary>
    ///     Returns the count of items in the group
    /// </summary>
    public long Count { get; }

    /// <summary>
    ///     Returns the count statistics for each attribute
    /// </summary>
    public IEnumerable<StatisticsResult> CountStatistics { get; }

    /// <summary>
    ///     Returns the min statistics for each attribute
    /// </summary>
    public IEnumerable<StatisticsResult> MinStatistics { get; }

    /// <summary>
    ///     Returns the max statistics for each attribute
    /// </summary>
    public IEnumerable<StatisticsResult> MaxStatistics { get; }

    /// <summary>
    ///     Returns the average value statistics for each attribute
    /// </summary>
    public IEnumerable<StatisticsResult> AvgStatistics { get; }
}