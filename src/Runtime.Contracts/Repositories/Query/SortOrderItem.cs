using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a sort order item.
/// </summary>
public class SortOrderItem
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="attributePath">Path to an attribute to filter for</param>
    /// <param name="sortOrder">Type of sort order</param>
    internal SortOrderItem(string attributePath, SortOrders sortOrder)
    {
        ArgumentValidation.ValidateString(nameof(attributePath), attributePath);

        AttributePath = attributePath;
        SortOrder = sortOrder;
    }

    /// <summary>
    ///     Gets the attribute name to sort by.
    /// </summary>
    public string AttributePath { get; }

    /// <summary>
    ///     How to sort the attribute.
    /// </summary>
    public SortOrders SortOrder { get; }
}