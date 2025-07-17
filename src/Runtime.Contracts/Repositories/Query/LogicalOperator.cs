namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents a logical operator for combining filters
/// </summary>
public enum LogicalOperator
{
    /// <summary>
    ///     Logical AND operator
    /// </summary>
    And = 0,

    /// <summary>
    ///     Logical OR operator
    /// </summary>
    Or = 1
}