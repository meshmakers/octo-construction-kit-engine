using Meshmakers.Octo.Runtime.Contracts.Formulas;
using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// Single implementation of <see cref="IFormulaEngine"/> wrapping <see cref="OctoExpression"/>
/// (mXparser). Consolidates the formula glue previously duplicated across
/// <c>ApplyDataPointMappingsNode</c> (mesh-adapter), <c>ExpressionValidationService</c>
/// (communication controller) and <c>FieldFilterResolver</c> (runtime query): the
/// ternary-to-if normalization, the null / NaN handling, and the cast-back ladder.
/// </summary>
internal sealed class FormulaEngine : IFormulaEngine
{
    /// <inheritdoc />
    public string NormalizeTernary(string expression) => ConvertTernaryToIf(expression);

    /// <inheritdoc />
    public FormulaSyntaxResult Validate(string expression, IReadOnlyDictionary<string, double> arguments)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new FormulaSyntaxResult(false, "Expression must not be empty.");
        }

        try
        {
            var normalized = ConvertTernaryToIf(expression);

            var expr = new OctoExpression(normalized);
            foreach (var (name, value) in arguments)
            {
                expr.addArguments(new Argument(name, value));
            }

            if (!expr.checkSyntax())
            {
                return new FormulaSyntaxResult(false, expr.getErrorMessage(),
                    NormalizedExpression: normalized);
            }

            var result = expr.calculate();
            if (double.IsNaN(result))
            {
                return new FormulaSyntaxResult(false, "Expression evaluates to NaN.",
                    NormalizedExpression: normalized);
            }

            return new FormulaSyntaxResult(true, Result: result, NormalizedExpression: normalized);
        }
        catch (Exception ex)
        {
            return new FormulaSyntaxResult(false, ex.Message);
        }
    }

    /// <inheritdoc />
    public FormulaSyntaxResult CheckSyntax(string expression, IReadOnlyCollection<string> argumentNames)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new FormulaSyntaxResult(false, "Expression must not be empty.");
        }

        try
        {
            var normalized = ConvertTernaryToIf(expression);

            var expr = new OctoExpression(normalized);
            foreach (var name in argumentNames)
            {
                expr.addArguments(new Argument(name, 0d));
            }

            return expr.checkSyntax()
                ? new FormulaSyntaxResult(true, NormalizedExpression: normalized)
                : new FormulaSyntaxResult(false, expr.getErrorMessage(), NormalizedExpression: normalized);
        }
        catch (Exception ex)
        {
            return new FormulaSyntaxResult(false, ex.Message);
        }
    }

    /// <inheritdoc />
    public double EvaluateRaw(string expression, IReadOnlyDictionary<string, double> arguments)
    {
        var expr = new OctoExpression(ConvertTernaryToIf(expression));
        foreach (var (name, value) in arguments)
        {
            expr.addArguments(new Argument(name, value));
        }

        return expr.calculate();
    }

    /// <inheritdoc />
    public object? Evaluate(string expression, IReadOnlyDictionary<string, double> arguments,
        FormulaResultType resultType)
    {
        var result = EvaluateRaw(expression, arguments);

        // NegativeInfinity is the engine's null sentinel; NaN means the formula could not produce
        // a value for this row. Both map to SQL NULL — never a half-baked number.
        if (double.IsNegativeInfinity(result) || double.IsNaN(result))
        {
            return null;
        }

        return resultType switch
        {
            FormulaResultType.Boolean => result != 0d,
            FormulaResultType.Int => (int)result,
            FormulaResultType.Int64 => (long)result,
            FormulaResultType.Double => result,
            FormulaResultType.DateTime => new DateTime((long)result),
            _ => throw new ArgumentOutOfRangeException(nameof(resultType), resultType, null)
        };
    }

    /// <summary>
    /// Converts C-style ternary operators (<c>cond ? a : b</c>) to mXparser's <c>if(cond, a, b)</c>
    /// syntax. Handles nesting by scanning for the matching ':' at the same paren depth level.
    /// Moved verbatim from the former duplicated copies in <c>ApplyDataPointMappingsNode</c> and
    /// <c>ExpressionValidationService</c>.
    /// </summary>
    internal static string ConvertTernaryToIf(string expression)
    {
        if (!expression.Contains('?')) return expression;

        while (true)
        {
            var qIdx = expression.IndexOf('?');
            if (qIdx < 0) break;

            // Find the matching ':' at the same paren depth
            var depth = 0;
            var colonIdx = -1;
            var nestedQCount = 0;
            for (var i = qIdx + 1; i < expression.Length; i++)
            {
                var ch = expression[i];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == '?' && depth == 0) nestedQCount++;
                else if (ch == ':' && depth == 0)
                {
                    if (nestedQCount == 0) { colonIdx = i; break; }
                    nestedQCount--;
                }
            }

            if (colonIdx < 0) break;

            // Find condition start: scan backwards for balanced parens or start
            var condStart = FindConditionStart(expression, qIdx);
            // Find false-branch end: scan forwards for balanced parens or end
            var falseEnd = FindFalseEnd(expression, colonIdx);

            var condition = expression[condStart..qIdx].Trim();
            var trueBranch = expression[(qIdx + 1)..colonIdx].Trim();
            var falseBranch = expression[(colonIdx + 1)..falseEnd].Trim();

            var replacement = $"if({condition}, {trueBranch}, {falseBranch})";
            expression = expression[..condStart] + replacement + expression[falseEnd..];
        }

        return expression;
    }

    private static int FindConditionStart(string s, int qIdx)
    {
        var depth = 0;
        for (var i = qIdx - 1; i >= 0; i--)
        {
            var ch = s[i];
            if (ch == ')') depth++;
            else if (ch == '(')
            {
                if (depth == 0) return i + 1;
                depth--;
            }
        }

        return 0;
    }

    private static int FindFalseEnd(string s, int colonIdx)
    {
        var depth = 0;
        for (var i = colonIdx + 1; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                if (depth == 0) return i;
                depth--;
            }
        }

        return s.Length;
    }
}
