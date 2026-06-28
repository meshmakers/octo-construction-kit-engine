namespace Meshmakers.Octo.Runtime.Contracts.Formulas;

/// <summary>
/// Result of validating a formula expression.
/// </summary>
/// <param name="IsValid">Whether the expression is syntactically valid and evaluates to a finite number.</param>
/// <param name="Error">Error message when invalid.</param>
/// <param name="Result">Evaluated result for the dummy test arguments, for preview / debugging.</param>
/// <param name="NormalizedExpression">The expression after ternary-to-if normalization.</param>
public record FormulaSyntaxResult(
    bool IsValid,
    string? Error = null,
    double? Result = null,
    string? NormalizedExpression = null);
