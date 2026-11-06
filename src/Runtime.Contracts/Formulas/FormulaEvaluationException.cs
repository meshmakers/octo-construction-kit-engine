using System;

namespace Meshmakers.Octo.Runtime.Contracts.Formulas;

/// <summary>
/// Thrown by <see cref="IFormulaEngine.EvaluateRaw"/> / <see cref="IFormulaEngine.Evaluate"/> when the
/// evaluation of a formula was aborted by an exception (e.g. an <see cref="OutOfMemoryException"/>)
/// instead of producing a result (AB#8331). It is never thrown for a formula that legitimately yields
/// no value — a NULL operand, the <c>null</c> constant, a division by zero (NaN); those still yield
/// NaN / <c>null</c>. A caller must not store NULL for this exception: the value is unknown, not empty.
/// </summary>
public sealed class FormulaEvaluationException : Exception
{
    /// <summary>
    /// Creates the exception for <paramref name="expression"/> with the engine's failure description.
    /// </summary>
    public FormulaEvaluationException(string expression, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Expression = expression;
    }

    /// <summary>The formula whose evaluation failed.</summary>
    public string Expression { get; }
}
