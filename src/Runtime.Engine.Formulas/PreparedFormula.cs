using System.Text.RegularExpressions;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// A formula parsed once for a fixed set of bound names and evaluated many times with different values
/// (AB#8331). Not thread-safe: <see cref="FormulaEngine"/> keeps its instances per thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which names are bound.</b> The former per-call expression bound every supplied value as an mXparser
/// argument. A prepared formula binds the supplied names that occur as an identifier in the formula
/// (<see cref="Identifiers"/>); the caller picks the variant for the set of identifiers the current values
/// supply (<see cref="FormulaShape"/>). The engine scans the identifiers itself instead of asking mXparser for
/// its missing arguments: that list leaves out supplied names that collide with a keyword the formula uses, and
/// mXparser 6.x split unknown names around built-in constants under implied multiplication (<c>value</c> became
/// <c>valu·e</c>; 4.4.2 does not, AB#8364). Results are therefore the same as before — an identifier that is not supplied stays
/// unbound (NaN for a name that is not a keyword), a supplied identifier that collides with a keyword the
/// formula uses fails the syntax check (NaN) — with one difference: a supplied name that does not occur in
/// the formula no longer matters. Before, a value named like a built-in (<c>e</c>, <c>pi</c>, <c>sin</c>, …)
/// made every formula of the row NaN, because mXparser refuses duplicate keywords even when unused.
/// </para>
/// <para>
/// <b>Exceptions.</b> mXparser 4.4.2 (AB#8364) lets an exception thrown inside <c>calculate()</c> — an
/// <see cref="OutOfMemoryException"/> included — propagate; it is wrapped into a
/// <see cref="FormulaEvaluationException"/> (inner exception kept), so a caller never stores NULL for an
/// aborted evaluation. (mXparser 6.x caught it and returned NaN; AB#8331 detected it in the error message.)
/// </para>
/// </remarks>
internal sealed class PreparedFormula
{
    private static readonly Regex IdentifierPattern =
        new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _expression;
    private readonly OctoExpression? _compiled;
    private readonly string[] _boundNames;
    private readonly Argument[] _arguments;

    private PreparedFormula(string expression, OctoExpression? compiled, string[] boundNames, Argument[] arguments)
    {
        _expression = expression;
        _compiled = compiled;
        _boundNames = boundNames;
        _arguments = arguments;
    }

    /// <summary>The names bound as mXparser arguments.</summary>
    internal IReadOnlyList<string> BoundNames => _boundNames;

    /// <summary>Whether the formula passed the syntax check with its names bound.</summary>
    internal bool IsValid => _compiled is not null;

    /// <summary>The distinct identifiers of <paramref name="normalizedExpression"/>, in order of appearance.</summary>
    internal static string[] Identifiers(string normalizedExpression)
    {
        var names = new List<string>();
        foreach (Match match in IdentifierPattern.Matches(normalizedExpression))
        {
            if (!names.Contains(match.Value, StringComparer.Ordinal))
            {
                names.Add(match.Value);
            }
        }

        return names.ToArray();
    }

    /// <summary>
    /// Parses <paramref name="normalizedExpression"/> (ternaries already converted) once with
    /// <paramref name="boundNames"/> as arguments. A formula that does not pass the syntax check is kept as
    /// invalid and evaluates to NaN, like before. <paramref name="additionalDefinitions"/> is a test seam
    /// (e.g. a function that throws).
    /// </summary>
    internal static PreparedFormula Create(string expression, string normalizedExpression,
        IReadOnlyList<string> boundNames, params PrimitiveElement[] additionalDefinitions)
    {
        var compiled = new OctoExpression(normalizedExpression);
        if (additionalDefinitions.Length > 0)
        {
            compiled.addDefinitions(additionalDefinitions);
        }

        var names = boundNames.ToArray();
        var arguments = new Argument[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            arguments[i] = new Argument(names[i], double.NaN);
            compiled.addArguments(arguments[i]);
        }

        return compiled.checkSyntax()
            ? new PreparedFormula(expression, compiled, names, arguments)
            : new PreparedFormula(expression, null, names, arguments);
    }

    /// <summary>
    /// Evaluates with the supplied values; <paramref name="values"/> must hold every bound name. Returns NaN
    /// when the formula is invalid; throws <see cref="FormulaEvaluationException"/> when the evaluation was
    /// aborted by an exception.
    /// </summary>
    internal double Evaluate(IReadOnlyDictionary<string, double> values)
    {
        if (_compiled is null)
        {
            return double.NaN;
        }

        for (var i = 0; i < _arguments.Length; i++)
        {
            _arguments[i].setArgumentValue(values[_boundNames[i]]);
        }

        try
        {
            return _compiled.calculate();
        }
        catch (Exception ex)
        {
            throw new FormulaEvaluationException(_expression,
                $"Evaluating formula '{_expression}' was aborted by {ex.GetType().Name}: {ex.Message}", ex);
        }
    }
}
