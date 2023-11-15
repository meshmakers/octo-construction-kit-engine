namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Interface for attribute value arrays
/// </summary>
/// <typeparam name="TValue"></typeparam>
public interface IAttributeValueArray<TValue> : IEnumerable<TValue>
{
    /// <summary>
    /// Adds the given value to the list
    /// </summary>
    /// <param name="value"></param>
    void Add(TValue value);

    /// <summary>
    /// Removes the given value from the list
    /// </summary>
    /// <param name="value"></param>
    void Remove(TValue value);

    /// <summary>
    /// Clears the list
    /// </summary>
    void Clear();
    
    /// <summary>
    /// Indexer for the list
    /// </summary>
    /// <param name="index">Index of the value to get or set</param>
    TValue this[int index] { get; set; }
    
    /// <summary>
    /// Returns the number of elements in the list
    /// </summary>
    int Count { get; }
}