namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
///     Implements <see cref="IAttributeValueList{TValue}" /> for string data type
/// </summary>
public class AttributeStringValueList : AttributeValueList<string, string>
{
    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="list"></param>
    public AttributeStringValueList(List<string> list)
        : base(list)
    {
    }

    /// <summary>
    ///     Constructor
    /// </summary>
    public AttributeStringValueList()
    {
    }

    /// <inheritdoc />
    protected override string CreateSubType(string valueBase)
    {
        return valueBase;
    }
}