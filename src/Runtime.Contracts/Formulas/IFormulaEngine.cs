using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.Formulas;

/// <summary>
/// Shared formula engine over the mXparser dialect used across OctoMesh
/// (adapter <c>DataPointMapping</c> expressions, archive computed columns, runtime-query
/// <c>@</c>-expressions). A single implementation wraps <c>OctoExpression</c>; consumers depend
/// on this interface so they need no direct mXparser reference.
/// </summary>
/// <remarks>
/// <b>NULL operands</b> (AB#8060; a replacement engine, e.g. AB#7995, must keep these semantics): a
/// caller binds an operand without a value (SQL NULL, an attribute the row does not carry) as
/// <see cref="double.NaN"/> instead of leaving it unbound. An unhandled NULL in arithmetic, a
/// comparison or a function yields NULL (<see cref="Evaluate"/> returns <c>null</c>). <c>if(cond, a, b)</c>
/// yields the branch it takes, so a NULL in the other branch does not matter; a NULL condition yields
/// NULL. <c>coalesce(x, default, …)</c> returns the first argument that is not NaN, so it replaces a
/// NULL operand but not the <c>null</c> constant (negative infinity); <c>isnull(x)</c> recognizes both
/// and is <c>1</c> for a NULL operand or the <c>null</c> constant, otherwise <c>0</c>.
/// </remarks>
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
    /// Checks an expression's syntax and that every referenced name is in
    /// <paramref name="argumentNames"/> — <b>without evaluating it</b>. Unlike <see cref="Validate"/>
    /// this never fails for a finite-but-undefined-at-probe-values result (e.g. a division that is
    /// zero only for the dummy values). Use for schema-time validation where only syntax and
    /// reference resolution matter.
    /// </summary>
    FormulaSyntaxResult CheckSyntax(string expression, IReadOnlyCollection<string> argumentNames);

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
