// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the result of an aggregation operation
/// </summary>
public class AggregationResult
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="count">Count of items in the group</param>
    /// <param name="countStatistics">Count statistics for each attribute</param>
    /// <param name="minStatistics">Min statistics for each attribute</param>
    /// <param name="maxStatistics">Max statistics for each attribute</param>
    /// <param name="avgStatistics">Average value statistics for each attribute</param>
    /// <param name="sumStatistics">Sum value statistics for each attribute</param>
    public AggregationResult(long count,
        IEnumerable<StatisticsResult> countStatistics,
        IEnumerable<StatisticsResult> minStatistics,
        IEnumerable<StatisticsResult> maxStatistics,
        IEnumerable<StatisticsResult> avgStatistics,
        IEnumerable<StatisticsResult> sumStatistics)
    {
        Count = count;
        CountStatistics = countStatistics;
        MinStatistics = minStatistics;
        MaxStatistics = maxStatistics;
        AvgStatistics = avgStatistics;
        SumStatistics = sumStatistics;
    }

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

    /// <summary>
    ///     Returns the sum value statistics for each attribute
    /// </summary>
    public IEnumerable<StatisticsResult> SumStatistics { get; }
}