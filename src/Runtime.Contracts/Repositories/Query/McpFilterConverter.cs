namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Converter for MCP filter DTOs to database layer filter classes
/// </summary>
public static class McpFilterConverter
{

    /// <summary>
    ///     Converts a strongly typed MCP FieldFilterDto to a FieldFilter
    /// </summary>
    /// <typeparam name="TFieldFilter">The type of the MCP field filter</typeparam>
    /// <param name="mcpFieldFilter">The MCP field filter to convert</param>
    /// <returns>The corresponding FieldFilter</returns>
    public static FieldFilter ConvertFieldFilter<TFieldFilter>(TFieldFilter mcpFieldFilter)
        where TFieldFilter : class
    {
        if (mcpFieldFilter == null)
            throw new ArgumentNullException(nameof(mcpFieldFilter));

        var fieldPathProperty = typeof(TFieldFilter).GetProperty("FieldPath");
        var operatorProperty = typeof(TFieldFilter).GetProperty("Operator");
        var valueProperty = typeof(TFieldFilter).GetProperty("Value");
        var secondValueProperty = typeof(TFieldFilter).GetProperty("SecondValue");

        if (fieldPathProperty == null || operatorProperty == null)
        {
            throw new ArgumentException("Invalid MCP field filter type", nameof(mcpFieldFilter));
        }

        var fieldPath = fieldPathProperty.GetValue(mcpFieldFilter)?.ToString();
        if (string.IsNullOrEmpty(fieldPath))
        {
            throw new ArgumentException("FieldPath is required", nameof(mcpFieldFilter));
        }

        var operatorValue = operatorProperty.GetValue(mcpFieldFilter);
        if (operatorValue == null)
            throw new ArgumentException("Operator is required", nameof(mcpFieldFilter));
        var fieldOperator = FilterConverter.ConvertFromMcpOperator(operatorValue);

        var primaryValue = valueProperty?.GetValue(mcpFieldFilter);
        var secondaryValue = secondValueProperty?.GetValue(mcpFieldFilter);

        return new FieldFilter(fieldPath!, fieldOperator, primaryValue, secondaryValue);
    }

    /// <summary>
    ///     Converts a strongly typed MCP EntityFilterDto to an EntityFilter
    /// </summary>
    /// <typeparam name="TEntityFilter">The type of the MCP entity filter</typeparam>
    /// <typeparam name="TFieldFilter">The type of the MCP field filter</typeparam>
    /// <param name="mcpEntityFilter">The MCP entity filter to convert</param>
    /// <returns>The corresponding EntityFilter</returns>
    public static EntityFilter ConvertEntityFilter<TEntityFilter, TFieldFilter>(TEntityFilter mcpEntityFilter)
        where TEntityFilter : class
        where TFieldFilter : class
    {
        if (mcpEntityFilter == null)
            throw new ArgumentNullException(nameof(mcpEntityFilter));

        var operatorProperty = typeof(TEntityFilter).GetProperty("Operator");
        var fieldsProperty = typeof(TEntityFilter).GetProperty("Fields");
        var nestedFiltersProperty = typeof(TEntityFilter).GetProperty("NestedFilters");

        if (operatorProperty == null)
        {
            throw new ArgumentException("Invalid MCP entity filter type", nameof(mcpEntityFilter));
        }

        var operatorValue = operatorProperty.GetValue(mcpEntityFilter);
        if (operatorValue == null)
            throw new ArgumentException("Operator is required", nameof(mcpEntityFilter));
        var logicalOperator = FilterConverter.ConvertFromMcpLogicalOperator(operatorValue);
        var entityFilter = new EntityFilter(logicalOperator);

        // Convert field filters
        if (fieldsProperty != null)
        {
            var fields = fieldsProperty.GetValue(mcpEntityFilter);
            if (fields is IEnumerable<TFieldFilter> fieldFilters)
            {
                foreach (var mcpFieldFilter in fieldFilters)
                {
                    var fieldFilter = ConvertFieldFilter(mcpFieldFilter);
                    entityFilter.AddField(fieldFilter);
                }
            }
        }

        // Convert nested filters
        if (nestedFiltersProperty != null)
        {
            var nestedFilters = nestedFiltersProperty.GetValue(mcpEntityFilter);
            if (nestedFilters is IEnumerable<TEntityFilter> nestedEntityFilters)
            {
                foreach (var mcpNestedFilter in nestedEntityFilters)
                {
                    var nestedFilter = ConvertEntityFilter<TEntityFilter, TFieldFilter>(mcpNestedFilter);
                    entityFilter.AddNestedFilter(nestedFilter);
                }
            }
        }

        return entityFilter;
    }


    /// <summary>
    ///     Converts a collection of strongly typed MCP FieldFilterDto to a collection of FieldFilter
    /// </summary>
    /// <typeparam name="TFieldFilter">The type of the MCP field filter</typeparam>
    /// <param name="mcpFieldFilters">The MCP field filters to convert</param>
    /// <returns>The corresponding collection of FieldFilter</returns>
    public static IEnumerable<FieldFilter> ConvertFieldFilters<TFieldFilter>(IEnumerable<TFieldFilter> mcpFieldFilters)
        where TFieldFilter : class
    {
        if (mcpFieldFilters == null)
            throw new ArgumentNullException(nameof(mcpFieldFilters));

        return mcpFieldFilters.Select(ConvertFieldFilter);
    }
}