namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a filter on the count of associations for a navigation pair.
/// Used for N:M association meta queries (totalCount, exists).
/// </summary>
public record AssociationCountFilter(FieldFilterOperator Operator, int ComparisonValue);
