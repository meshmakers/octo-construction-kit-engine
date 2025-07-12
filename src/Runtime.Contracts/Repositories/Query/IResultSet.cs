namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a result set.
/// </summary>
/// <typeparam name="TDocument">Type of document</typeparam>
public interface IResultSet<out TDocument>
{
    /// <summary>
    ///     Returns the total number of items in the result set.
    /// </summary>
    long TotalCount { get; }

    /// <summary>
    ///     Returns the items in the result set.
    /// </summary>
    IEnumerable<TDocument> Items { get; }

    /// <summary>
    ///     Returns the aggregation input for the result set.
    /// </summary>
    public AggregationResult? AggregationResult { get; }

    /// <summary>
    ///     Returns the aggregation results for a field aggregation operation.
    /// </summary>
    public IEnumerable<FieldAggregationResult>? FieldAggregationResult { get; }
}