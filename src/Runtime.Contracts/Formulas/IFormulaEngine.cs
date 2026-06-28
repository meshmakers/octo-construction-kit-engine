using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.Formulas;

/// <summary>
/// Shared formula engine over the mXparser dialect used across OctoMesh
/// (adapter <c>DataPointMapping</c> expressions, archive computed columns, runtime-query
/// <c>@</c>-expressions). A single implementation wraps <c>OctoExpression</c>; consumers depend
/// on this interface so they need no direct mXparser reference.
/// </summary>
public interface IFormulaEngine
{
    /// <summary>
    /// Converts C-style ternary operators (<c>cond ? a : b</c>) to mXparser <c>if(cond, a, b)</c>
    /// syntax. A no-op for expressions that contain no ternary.
    /// </summary>
    string NormalizeTernary(string expression);

    /// <summary>
    /// Validates an expression by binding the supplied test values to their argument names and
    /// checking syntax plus that it evaluates to a finite number. The keys of
    /// <paramref name="arguments"/> are the column / variable names the formula is allowed to
    /// reference; the values are test bindings (e.g. <c>42.0</c> per column, or a caller-supplied
    /// preview value). An expression referencing a name not present here fails the syntax check.
    /// </summary>
    FormulaSyntaxResult Validate(string expression, IReadOnlyDictionary<string, double> arguments);

    /// <summary>
    /// Evaluates the expression to a raw <see cref="double"/>. Returns <see cref="double.NaN"/> on
    /// evaluation failure; <see cref="double.NegativeInfinity"/> is the engine's <c>null</c> sentinel.
    /// </summary>
    double EvaluateRaw(string expression, IReadOnlyDictionary<string, double> arguments);

    /// <summary>
    /// Evaluates the expression and casts the <see cref="double"/> result back to
    /// <paramref name="resultType"/>. Returns <c>null</c> for the <c>null</c> sentinel
    /// (<see cref="double.NegativeInfinity"/>) or a <see cref="double.NaN"/> result.
    /// </summary>
    object? Evaluate(string expression, IReadOnlyDictionary<string, double> arguments,
        FormulaResultType resultType);
}
