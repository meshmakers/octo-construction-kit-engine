namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Interface for attribute value arrays
/// </summary>
/// <typeparam name="TValue"></typeparam>
public interface IAttributeValueList<TValue> : IList<TValue>
{
    /// <summary>
    /// Adds the elements of the given collection to the end of this list. If
    /// required, the capacity of the list is increased to twice the previous
    /// capacity or the new size, whichever is larger.
    /// </summary>
    /// <param name="collection">The list that need to be added.</param>
    void AddRange(IEnumerable<TValue> collection);
    
    /// <summary>
    /// This method removes all items which matches the predicate.
    /// The complexity is O(n).
    /// </summary>
    /// <param name="match">The match delegate function</param>
    /// <returns>Amount of matches</returns>
    int RemoveAll(Predicate<TValue> match);
}