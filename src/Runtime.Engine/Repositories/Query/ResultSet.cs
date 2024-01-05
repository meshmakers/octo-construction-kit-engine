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
    /// <param name="totalCount">Total count of items based on query.</param>
    /// <param name="groupingResults">The optional grouping results if requested.</param>
    public ResultSet(IEnumerable<TDocument> result, long totalCount, IEnumerable<GroupingResult>? groupingResults)
    {
        Items = result;
        TotalCount = totalCount;
        Grouping = groupingResults;
    }

    /// <inheritdoc />
    public long TotalCount { get; }

    /// <inheritdoc />
    public IEnumerable<TDocument> Items { get; }

    /// <inheritdoc />
    public IEnumerable<GroupingResult>? Grouping { get; }
}