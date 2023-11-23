using System.Collections;

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// 
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class AttributeRecordValueArray<TValue> : IAttributeValueArray<TValue>, IAttributeValueList
    where TValue: RtRecord, new()
{
    private readonly IList<RtRecord> _list;

    /// <summary>
    /// Returns the inner list
    /// </summary>
    public IList<RtRecord> InnerList => _list;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="list"></param>
    public AttributeRecordValueArray(IList<RtRecord> list)
    {
        _list = list;
    }
    
    /// <summary>
    /// Constructor
    /// </summary>
    public AttributeRecordValueArray()
    {
        _list = new List<RtRecord>();
    }
    
    /// <inheritdoc />
    public IEnumerator<TValue> GetEnumerator()
    {
        return _list.Select(CreateSubType).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <inheritdoc />
    public void Add(TValue value)
    {
        _list.Add(value);
    }

    /// <inheritdoc />
    public void Remove(TValue value)
    {
        _list.Remove(value);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _list.Clear();
    }
    
    /// <inheritdoc />
    public TValue this[int index]
    {
        get
        {
            var o = _list[index];
            return CreateSubType(o);
        }
        set => _list[index] = value;
    }

    private static TValue CreateSubType(RtRecord o)
    {
        if (o is TValue value)
        {
            return value;
        }

        var x = (TValue?)Activator.CreateInstance(typeof(TValue), o);
        if (x == null)
        {
            throw InvalidAttributeValueException.CannotActivateInstance(typeof(TValue));
        }

        return x;
    }

    /// <inheritdoc />
    public int Count => _list.Count;
}