namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the field group by type.
/// </summary>
public class FieldGroupBy
{
    private readonly List<string> _avgAttributeNames;
    private readonly List<string> _countAttributeNames;
    private readonly List<string> _maxAttributeNames;
    private readonly List<string> _minAttributeNames;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="groupByAttributeNames">Attribute names to group by</param>
    public FieldGroupBy(IEnumerable<string> groupByAttributeNames)
    {
        GroupByAttributeNameList = groupByAttributeNames;
        _countAttributeNames = new List<string>();
        _minAttributeNames = new List<string>();
        _maxAttributeNames = new List<string>();
        _avgAttributeNames = new List<string>();
    }

    /// <summary>
    ///     Attribute names to group by
    /// </summary>
    public IEnumerable<string> GroupByAttributeNameList { get; }

    /// <summary>
    ///     Attribute names whose existence to count, NULL values are not counted.
    /// </summary>
    public IEnumerable<string> CountAttributeNameList => _countAttributeNames;

    /// <summary>
    ///     Attributes names whose maximum value is to be determined.
    /// </summary>
    public IEnumerable<string> MaxValueAttributeNameList => _maxAttributeNames;

    /// <summary>
    ///     Attributes names whose minimum value is to be determined.
    /// </summary>
    public IEnumerable<string> MinValueAttributeNameList => _minAttributeNames;

    /// <summary>
    ///     Attributes names whose average value is to be determined.
    /// </summary>
    public IEnumerable<string> AvgAttributeNameList => _avgAttributeNames;

    /// <summary>
    ///     Attributes names whose amount of non-null values is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy CountAttributeNames(params string[] attributeNames)
    {
        _countAttributeNames.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose min value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy MinAttributeNames(params string[] attributeNames)
    {
        _minAttributeNames.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose max value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy MaxAttributeNames(params string[] attributeNames)
    {
        _maxAttributeNames.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose average value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy AvgAttributeNames(params string[] attributeNames)
    {
        _avgAttributeNames.AddRange(attributeNames);
        return this;
    }
}