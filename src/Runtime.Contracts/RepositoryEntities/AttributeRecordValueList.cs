
namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Implements <see cref="IAttributeValueList{TValue}"/> for record data types
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class AttributeRecordValueList<TValue> : AttributeValueList<RtRecord, TValue>, IAttributeRecordValueArray
    where TValue : RtRecord, new()
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="list"></param>
    public AttributeRecordValueList(IList<RtRecord> list)
        : base(list)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    public AttributeRecordValueList()
    {
    }


    /// <inheritdoc />
    protected override TValue CreateSubType(RtRecord o)
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
    public IList<RtRecord> RecordList => InnerList;
}