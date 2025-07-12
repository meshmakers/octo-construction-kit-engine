using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Query;

/// <summary>
///     Implements a result set that is pageable
/// </summary>
/// <typeparam name="TDocument">Type of document</typeparam>
public class ResultSet<TDocument> : IResultSet<TDocument>
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="result">Items of the current page</param>
    /// <param name="totalCount">Total count of items based on a query.</param>
    /// <param name="aggregationResults">The optional aggregation results if requested.</param>
    /// <param name="fieldAggregationResult">The optional field aggregation results if requested.</param>
    public ResultSet(IEnumerable<TDocument> result, long totalCount,
        AggregationResult? aggregationResults,
        IEnumerable<FieldAggregationResult>? fieldAggregationResult)
    {
        Items = result;
        TotalCount = totalCount;
        AggregationResult = aggregationResults;
        FieldAggregationResult = fieldAggregationResult;
    }

    /// <inheritdoc />
    public long TotalCount { get; }

    /// <inheritdoc />
    public IEnumerable<TDocument> Items { get; }

    /// <inheritdoc />
    public AggregationResult? AggregationResult { get; }

    /// <inheritdoc />
    public IEnumerable<FieldAggregationResult>? FieldAggregationResult { get; }
}