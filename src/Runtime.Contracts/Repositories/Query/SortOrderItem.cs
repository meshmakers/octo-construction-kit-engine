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
    /// <param name="attributeName">Attribute name</param>
    /// <param name="sortOrder">Type of sort order</param>
    internal SortOrderItem(string attributeName, SortOrders sortOrder)
    {
        ArgumentValidation.ValidateString(nameof(attributeName), attributeName);

        AttributeName = attributeName;
        SortOrder = sortOrder;
    }

    /// <summary>
    ///     Gets the attribute name to sort by.
    /// </summary>
    public string AttributeName { get; }

    /// <summary>
    ///     How to sort the attribute.
    /// </summary>
    public SortOrders SortOrder { get; }
}