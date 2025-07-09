namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the field group by type.
/// </summary>
public class FieldGroupBy
{
    private readonly List<string> _avgAttributePaths;
    private readonly List<string> _sumAttributePaths;
    private readonly List<string> _countAttributePaths;
    private readonly List<string> _maxAttributePaths;
    private readonly List<string> _minAttributePaths;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="groupByAttributePaths">Attribute names to group by</param>
    public FieldGroupBy(IEnumerable<string> groupByAttributePaths)
    {
        GroupByAttributePathList = groupByAttributePaths;
        _countAttributePaths = new List<string>();
        _minAttributePaths = new List<string>();
        _maxAttributePaths = new List<string>();
        _avgAttributePaths = new List<string>();
        _sumAttributePaths = new List<string>();
    }

    /// <summary>
    ///     Attribute names to group by
    /// </summary>
    public IEnumerable<string> GroupByAttributePathList { get; }

    /// <summary>
    ///     Attribute names whose existence to count, NULL values are not counted.
    /// </summary>
    public IEnumerable<string> CountAttributePathList => _countAttributePaths;

    /// <summary>
    ///     Attributes names whose maximum value is to be determined.
    /// </summary>
    public IEnumerable<string> MaxValueAttributePathList => _maxAttributePaths;

    /// <summary>
    ///     Attributes names whose minimum value is to be determined.
    /// </summary>
    public IEnumerable<string> MinValueAttributePathList => _minAttributePaths;

    /// <summary>
    ///     Attributes names whose average value is to be determined.
    /// </summary>
    public IEnumerable<string> AvgAttributePathList => _avgAttributePaths;

    /// <summary>
    ///     Attributes names whose sum value is to be determined.
    /// </summary>
    public IEnumerable<string> SumAttributePathList => _sumAttributePaths;

    /// <summary>
    ///     Attributes names whose amount of non-null values is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy CountAttributeNames(params string[] attributeNames)
    {
        _countAttributePaths.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose min value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy MinAttributeNames(params string[] attributeNames)
    {
        _minAttributePaths.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose max value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy MaxAttributeNames(params string[] attributeNames)
    {
        _maxAttributePaths.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose average value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy AvgAttributeNames(params string[] attributeNames)
    {
        _avgAttributePaths.AddRange(attributeNames);
        return this;
    }

    /// <summary>
    ///     Attributes names whose sum value is to be determined.
    /// </summary>
    /// <param name="attributeNames">Attribute names</param>
    /// <returns></returns>
    public FieldGroupBy SumAttributeNames(params string[] attributeNames)
    {
        _avgAttributePaths.AddRange(attributeNames);
        return this;
    }
}