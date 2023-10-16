using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Query;

/// <summary>
/// Implements a result set that is pageable
/// </summary>
/// <typeparam name="TDocument">Type of document</typeparam>
public class ResultSet<TDocument> : IResultSet<TDocument>
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="result">Items of the current page</param>
    /// <param name="totalCount">Total count of items based on query.</param>
    public ResultSet(IEnumerable<TDocument> result, long totalCount)
    {
        Items = result;
        TotalCount = totalCount;
    }

    /// <inheritdoc />
    public long TotalCount { get; }

    /// <inheritdoc />
    public IEnumerable<TDocument> Items { get; }
}
