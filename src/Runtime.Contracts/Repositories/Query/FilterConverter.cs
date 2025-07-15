namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Utility class for converting between different filter representations
/// </summary>
public static class FilterConverter
{
    /// <summary>
    ///     Converts a MCP FilterOperatorDto to a FieldFilterOperator
    /// </summary>
    /// <param name="mcpOperator">The MCP operator to convert</param>
    /// <returns>The corresponding FieldFilterOperator</returns>
    public static FieldFilterOperator ConvertFromMcpOperator(object mcpOperator)
    {
        if (mcpOperator == null)
            throw new ArgumentNullException(nameof(mcpOperator));

        return mcpOperator.ToString() switch
        {
            "Equals" => FieldFilterOperator.Equals,
            "NotEquals" => FieldFilterOperator.NotEquals,
            "Contains" => FieldFilterOperator.Contains,
            "StartsWith" => FieldFilterOperator.StartsWith,
            "EndsWith" => FieldFilterOperator.EndsWith,
            "GreaterThan" => FieldFilterOperator.GreaterThan,
            "GreaterThanOrEqual" => FieldFilterOperator.GreaterEqualThan,
            "LessThan" => FieldFilterOperator.LessThan,
            "LessThanOrEqual" => FieldFilterOperator.LessEqualThan,
            "Between" => FieldFilterOperator.Between,
            "In" => FieldFilterOperator.In,
            "NotIn" => FieldFilterOperator.NotIn,
            "IsNull" => FieldFilterOperator.IsNull,
            "IsNotNull" => FieldFilterOperator.IsNotNull,
            "Regex" => FieldFilterOperator.MatchRegEx,
            _ => throw new ArgumentException($"Unknown MCP operator: {mcpOperator}", nameof(mcpOperator))
        };
    }

    /// <summary>
    ///     Converts a MCP LogicalOperatorDto to a LogicalOperator
    /// </summary>
    /// <param name="mcpOperator">The MCP logical operator to convert</param>
    /// <returns>The corresponding LogicalOperator</returns>
    public static LogicalOperator ConvertFromMcpLogicalOperator(object mcpOperator)
    {
        if (mcpOperator == null)
            throw new ArgumentNullException(nameof(mcpOperator));

        return mcpOperator.ToString() switch
        {
            "And" => LogicalOperator.And,
            "Or" => LogicalOperator.Or,
            _ => throw new ArgumentException($"Unknown MCP logical operator: {mcpOperator}", nameof(mcpOperator))
        };
    }

    /// <summary>
    ///     Converts a FieldFilterOperator to a MCP FilterOperatorDto string
    /// </summary>
    /// <param name="fieldOperator">The field operator to convert</param>
    /// <returns>The corresponding MCP operator string</returns>
    public static string ConvertToMcpOperator(FieldFilterOperator fieldOperator)
    {
        return fieldOperator switch
        {
            FieldFilterOperator.Equals => "Equals",
            FieldFilterOperator.NotEquals => "NotEquals",
            FieldFilterOperator.Contains => "Contains",
            FieldFilterOperator.StartsWith => "StartsWith",
            FieldFilterOperator.EndsWith => "EndsWith",
            FieldFilterOperator.GreaterThan => "GreaterThan",
            FieldFilterOperator.GreaterEqualThan => "GreaterThanOrEqual",
            FieldFilterOperator.LessThan => "LessThan",
            FieldFilterOperator.LessEqualThan => "LessThanOrEqual",
            FieldFilterOperator.Between => "Between",
            FieldFilterOperator.In => "In",
            FieldFilterOperator.NotIn => "NotIn",
            FieldFilterOperator.IsNull => "IsNull",
            FieldFilterOperator.IsNotNull => "IsNotNull",
            FieldFilterOperator.MatchRegEx => "Regex",
            FieldFilterOperator.Like => "Contains", // Map Like to Contains for MCP
            FieldFilterOperator.AnyEq => "Equals", // Map AnyEq to Equals for MCP
            FieldFilterOperator.AnyLike => "Contains", // Map AnyLike to Contains for MCP
            FieldFilterOperator.Match => "Equals", // Map Match to Equals for MCP
            _ => throw new ArgumentException($"Unknown field operator: {fieldOperator}", nameof(fieldOperator))
        };
    }

    /// <summary>
    ///     Converts a LogicalOperator to a MCP LogicalOperatorDto string
    /// </summary>
    /// <param name="logicalOperator">The logical operator to convert</param>
    /// <returns>The corresponding MCP logical operator string</returns>
    public static string ConvertToMcpLogicalOperator(LogicalOperator logicalOperator)
    {
        return logicalOperator switch
        {
            LogicalOperator.And => "And",
            LogicalOperator.Or => "Or",
            _ => throw new ArgumentException($"Unknown logical operator: {logicalOperator}", nameof(logicalOperator))
        };
    }
}