using System.Collections;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Represents a list of attribute values
/// </summary>
/// <typeparam name="TValue">An inherited type of the base type</typeparam>
/// <typeparam name="TValueBase">The base type of the data type</typeparam>
public abstract class AttributeValueList<TValueBase, TValue> : IAttributeValueList<TValue>
    where TValue : TValueBase
{
    /// <summary>
    ///     Creates a new instance of <see cref="AttributeValueList{TValueBase,TValue}" />
    /// </summary>
    /// <param name="values">The inner list</param>
    protected AttributeValueList(List<TValueBase> values)
    {
        InnerList = values;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="AttributeValueList{TValueBase,TValue}" />
    /// </summary>
    protected AttributeValueList()
    {
        InnerList = new List<TValueBase>();
    }

    internal List<TValueBase> InnerList { get; }

    /// <inheritdoc />
    public void Add(TValue value)
    {
        InnerList.Add(value);
    }

    /// <inheritdoc />
    public void CopyTo(TValue[] array, int arrayIndex)
    {
        InnerList.Select(CreateSubType).ToList().CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    public bool Remove(TValue value)
    {
        return InnerList.Remove(value);
    }

    /// <inheritdoc />
    public void Clear()
    {
        InnerList.Clear();
    }

    /// <inheritdoc />
    public bool Contains(TValue item)
    {
        return InnerList.Contains(item);
    }

    /// <inheritdoc />
    public int IndexOf(TValue item)
    {
        return InnerList.IndexOf(item);
    }

    /// <inheritdoc />
    public void Insert(int index, TValue item)
    {
        InnerList.Insert(index, item);
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        InnerList.RemoveAt(index);
    }

    /// <inheritdoc />
    public TValue this[int index]
    {
        get
        {
            var o = InnerList[index];
            return CreateSubType(o);
        }
        set => InnerList[index] = value;
    }

    /// <inheritdoc />
    public int Count => InnerList.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void AddRange(IEnumerable<TValue> collection)
    {
        InnerList.AddRange(collection.Cast<TValueBase>());
    }

    /// <inheritdoc />
    public int RemoveAll(Predicate<TValue> match)
    {
        return InnerList.RemoveAll(t => match(CreateSubType(t)));
    }

    /// <inheritdoc />
    public int FindIndex(Predicate<TValue> match)
    {
        return InnerList.FindIndex(t => match(CreateSubType(t)));
    }

    /// <inheritdoc />
    public IEnumerator<TValue> GetEnumerator()
    {
        return InnerList.Select(CreateSubType).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    ///     Creates a new instance of <typeparamref name="TValue" /> from the given <typeparamref name="TValueBase" />
    /// </summary>
    /// <param name="valueBase">The base value</param>
    /// <returns></returns>
    protected abstract TValue CreateSubType(TValueBase valueBase);
}