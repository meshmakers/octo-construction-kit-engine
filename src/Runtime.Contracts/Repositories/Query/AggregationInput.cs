namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents aggregation operations
/// </summary>
public class AggregationInput
{
    private readonly List<string> _avgAttributePaths;
    private readonly List<string> _sumAttributePaths;
    private readonly List<string> _countAttributePaths;
    private readonly List<string> _maxAttributePaths;
    private readonly List<string> _minAttributePaths;

    /// <summary>
    ///     Constructor
    /// </summary>
    public AggregationInput()
    {
        _countAttributePaths = new List<string>();
        _minAttributePaths = new List<string>();
        _maxAttributePaths = new List<string>();
        _avgAttributePaths = new List<string>();
        _sumAttributePaths = new List<string>();
    }

    /// <summary>
    ///     Attribute paths whose existence to count, NULL values are not counted.
    /// </summary>
    public IEnumerable<string> CountAttributePathList => _countAttributePaths;

    /// <summary>
    ///     Attributes paths whose maximum value is to be determined.
    /// </summary>
    public IEnumerable<string> MaxValueAttributePathList => _maxAttributePaths;

    /// <summary>
    ///     Attributes paths whose minimum value is to be determined.
    /// </summary>
    public IEnumerable<string> MinValueAttributePathList => _minAttributePaths;

    /// <summary>
    ///     Attributes paths whose average value is to be determined.
    /// </summary>
    public IEnumerable<string> AvgAttributePathList => _avgAttributePaths;

    /// <summary>
    ///     Attributes paths whose sum value is to be determined.
    /// </summary>
    public IEnumerable<string> SumAttributePathList => _sumAttributePaths;

    /// <summary>
    ///     Attributes paths whose number of non-null values is to be determined.
    /// </summary>
    /// <param name="attributePaths">Path of attribute</param>
    /// <returns></returns>
    public AggregationInput CountAttributePaths(params string[] attributePaths)
    {
        _countAttributePaths.AddRange(attributePaths);
        return this;
    }

    /// <summary>
    ///     Attributes paths whose min value is to be determined.
    /// </summary>
    /// <param name="attributePaths">Path of attribute</param>
    /// <returns></returns>
    public AggregationInput MinAttributePaths(params string[] attributePaths)
    {
        _minAttributePaths.AddRange(attributePaths);
        return this;
    }

    /// <summary>
    ///     Attributes paths whose max value is to be determined.
    /// </summary>
    /// <param name="attributePaths">Path of attribute</param>
    /// <returns></returns>
    public AggregationInput MaxAttributePaths(params string[] attributePaths)
    {
        _maxAttributePaths.AddRange(attributePaths);
        return this;
    }

    /// <summary>
    ///     Attributes paths whose average value is to be determined.
    /// </summary>
    /// <param name="attributePaths">Path of attribute</param>
    /// <returns></returns>
    public AggregationInput AvgAttributePaths(params string[] attributePaths)
    {
        _avgAttributePaths.AddRange(attributePaths);
        return this;
    }

    /// <summary>
    ///     Attributes paths whose sum value is to be determined.
    /// </summary>
    /// <param name="attributePaths">Path of attribute</param>
    /// <returns></returns>
    public AggregationInput SumAttributePaths(params string[] attributePaths)
    {
        _sumAttributePaths.AddRange(attributePaths);
        return this;
    }
}