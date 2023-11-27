namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Implements <see cref="IAttributeValueList{TValue}"/> for primitive data types
/// </summary>
/// <typeparam name="TValue"></typeparam>
public class AttributePrimitiveValueList<TValue> : AttributeValueList<TValue, TValue>
    where TValue: struct
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="list"></param>
    public AttributePrimitiveValueList(List<TValue> list)
        : base(list)
    {
    }

    /// <summary>
    /// Constructor
    /// </summary>
    public AttributePrimitiveValueList()
    {
    }

    /// <inheritdoc />
    protected override TValue CreateSubType(TValue valueBase)
    {
        return valueBase;
    }
}