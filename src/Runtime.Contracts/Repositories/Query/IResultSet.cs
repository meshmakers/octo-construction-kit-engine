namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a result set.
/// </summary>
/// <typeparam name="TDocument">Type of document</typeparam>
public interface IResultSet<out TDocument>
{
    /// <summary>
    /// Returns the total number of items in the result set.
    /// </summary>
    long TotalCount { get; }
    
    /// <summary>
    /// Returns the items in the result set.
    /// </summary>
    IEnumerable<TDocument> Items { get; }
    
    /// <summary>
    /// Returns the grouping result
    /// </summary>
    public IEnumerable<GroupingResult>? Grouping { get; }
}