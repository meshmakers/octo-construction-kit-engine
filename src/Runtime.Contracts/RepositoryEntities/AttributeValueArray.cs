using System.Collections;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents a list of attribute values
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class AttributeValueArray<TValue> : IAttributeValueArray<TValue>
{
    private readonly IList<TValue> _values;

    /// <summary>
    /// Creates a new instance of <see cref="AttributeValueArray{TValue}"/>
    /// </summary>
    /// <param name="values">The inner list</param>
    public AttributeValueArray(IList<TValue> values)
    {
        _values = values;
    }

    /// <summary>
    /// Creates a new instance of <see cref="AttributeValueArray{TValue}"/>
    /// </summary>
    public AttributeValueArray()
    {
        _values = new List<TValue>();
    }

    internal IList<TValue> InnerList => _values;

    /// <summary>
    /// Adds the given value to the list
    /// </summary>
    /// <param name="value"></param>
    public void Add(TValue value)
    {
        _values.Add(value);
    }

    /// <summary>
    /// Removes the given value from the list
    /// </summary>
    /// <param name="value"></param>
    public void Remove(TValue value)
    {
        _values.Remove(value);
    }

    /// <summary>
    /// Clears the list
    /// </summary>
    public void Clear()
    {
        _values.Clear();
    }

    /// <inheritdoc />
    public TValue this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    /// <inheritdoc />
    public int Count => _values.Count;

    /// <inheritdoc />
    public IEnumerator<TValue> GetEnumerator()
    {
        return _values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}