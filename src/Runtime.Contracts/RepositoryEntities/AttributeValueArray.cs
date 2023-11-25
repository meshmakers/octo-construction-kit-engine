using System.Collections;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents a list of attribute values
/// </summary>
/// <typeparam name="TValue">An inherited type of the base type</typeparam>
/// <typeparam name="TValueBase">The base type of the data type</typeparam>
public abstract class AttributeValueList<TValueBase, TValue> : IAttributeValueList<TValue>
    where TValue : TValueBase
{
    private readonly List<TValueBase> _values;

    /// <summary>
    /// Creates a new instance of <see cref="AttributeValueList{TValueBase,TValue}"/>
    /// </summary>
    /// <param name="values">The inner list</param>
    protected AttributeValueList(IList<TValueBase> values)
    {
        _values = new List<TValueBase>(values);
    }

    /// <summary>
    /// Creates a new instance of <see cref="AttributeValueList{TValueBase,TValue}"/>
    /// </summary>
    protected AttributeValueList()
    {
        _values = new List<TValueBase>();
    }

    internal IList<TValueBase> InnerList => _values;


    /// <inheritdoc />
    public void Add(TValue value)
    {
        _values.Add(value);
    }

    /// <inheritdoc />
    public void CopyTo(TValue[] array, int arrayIndex)
    {
        _values.Select(CreateSubType).ToList().CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    public bool Remove(TValue value)
    {
        return _values.Remove(value);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _values.Clear();
    }

    /// <inheritdoc />
    public bool Contains(TValue item)
    {
        return _values.Contains(item);
    }

    /// <inheritdoc />
    public int IndexOf(TValue item)
    {
        return _values.IndexOf(item);
    }

    /// <inheritdoc />
    public void Insert(int index, TValue item)
    {
        _values.Insert(index, item);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        _values.RemoveAt(index);
    }

    /// <inheritdoc />
    public TValue this[int index]
    {
        get
        {
            var o = _values[index];
            return CreateSubType(o);
        }
        set => _values[index] = value;
    }

    /// <inheritdoc />
    public int Count => _values.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void AddRange(IEnumerable<TValue> collection)
    {
        _values.AddRange(collection.Cast<TValueBase>());
    }

    /// <inheritdoc />
    public int RemoveAll(Predicate<TValue> match)
    {
        return _values.RemoveAll(t => match(CreateSubType(t)));
    }

    /// <inheritdoc />
    public IEnumerator<TValue> GetEnumerator()
    {
        return _values.Select(CreateSubType).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Creates a new instance of <typeparamref name="TValue"/> from the given <typeparamref name="TValueBase"/>
    /// </summary>
    /// <param name="valueBase">The base value</param>
    /// <returns></returns>
    protected abstract TValue CreateSubType(TValueBase valueBase);
}