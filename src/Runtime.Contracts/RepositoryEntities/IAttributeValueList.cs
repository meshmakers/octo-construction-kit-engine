namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Interface for attribute value arrays
/// </summary>
/// <typeparam name="TValue"></typeparam>
public interface IAttributeValueList<TValue> : IList<TValue>
{
    
    /// <summary>
    /// This method removes all items which matches the predicate.
    /// The complexity is O(n).
    /// </summary>
    /// <param name="match">The match delegate function</param>
    /// <returns>Amount of matches</returns>
    int RemoveAll(Predicate<TValue> match);
}