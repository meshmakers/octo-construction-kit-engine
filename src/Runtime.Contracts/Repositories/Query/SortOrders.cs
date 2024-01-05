namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the sort order type.
/// </summary>
public enum SortOrders
{
    /// <summary>
    ///     Default sorting based on data source type
    /// </summary>
    Default = 0,

    /// <summary>
    ///     Sort ascending
    /// </summary>
    Ascending = 1,

    /// <summary>
    ///     Sort descending
    /// </summary>
    Descending = 2
}