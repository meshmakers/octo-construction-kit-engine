namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents the input for field aggregation operations.
/// </summary>
public class FieldAggregationInput : AggregationInput
{
    /// <summary>
    /// Constructs a new instance of <see cref="FieldAggregationInput"/> with the specified group by attribute paths.
    /// </summary>
    /// <param name="groupByAttributePaths">Group by attribute paths.</param>
    public FieldAggregationInput(IEnumerable<string> groupByAttributePaths)
    {
        GroupByAttributePathList = groupByAttributePaths;
    }

    /// <summary>
    ///     Attribute names to group by
    /// </summary>
    public IEnumerable<string> GroupByAttributePathList { get; }

    /// <summary>
    ///     If true, resolve enum integer values to their label names in groupBy keys.
    ///     Defaults to true.
    /// </summary>
    public bool ResolveEnumValuesToNames { get; set; } = true;
}