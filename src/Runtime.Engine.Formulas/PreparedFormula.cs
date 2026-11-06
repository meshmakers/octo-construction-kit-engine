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
/// supply (<see cref="FormulaShape"/>). mXparser's own list of missing arguments cannot be used for this: with
/// implied multiplication it splits an unknown name around built-in constants (<c>value</c> becomes
/// <c>valu·e</c>). Results are therefore the same as before — an identifier that is not supplied stays
/// unbound (NaN for a name that is not a keyword), a supplied identifier that collides with a keyword the
/// formula uses fails the syntax check (NaN) — with one difference: a supplied name that does not occur in
/// the formula no longer matters. Before, a value named like a built-in (<c>e</c>, <c>pi</c>, <c>sin</c>, …)
/// made every formula of the row NaN, because mXparser refuses duplicate keywords even when unused.
/// </para>
/// <para>
/// <b>Exceptions.</b> mXparser catches every exception inside <c>calculate()</c> — an
/// <see cref="OutOfMemoryException"/> included — and returns NaN, which a caller would store as NULL. A NaN
/// result whose error message carries an exception is therefore turned into a
/// <see cref="FormulaEvaluationException"/>.
/// </para>
/// </remarks>
internal sealed class PreparedFormula
{
    // mXparser renders a caught exception as "...: <SimpleTypeName>: <message>" (the surrounding text is
    // localised, the type name is not).
    private static readonly Regex CaughtExceptionPattern =
        new(@"\b(?<type>[A-Za-z_][A-Za-z0-9_]*Exception): ", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    /// <summary>
    /// Evaluations since the formula was parsed. mXparser 6.1.1 never marks an expression that contains
    /// <c>if</c> as fully compiled and appends its compiled elements on every <c>calculate()</c> (~400 bytes
    /// each, never released); <see cref="FormulaShape"/> therefore parses the formula again after
    /// <see cref="FormulaShape.MaxEvaluationsPerParse"/> evaluations.
    /// </summary>
    internal int Evaluations { get; private set; }

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

        Evaluations++;
        for (var i = 0; i < _arguments.Length; i++)
        {
            _arguments[i].setArgumentValue(values[_boundNames[i]]);
        }

        var result = _compiled.calculate();
        if (double.IsNaN(result))
        {
            var error = _compiled.getErrorMessage();
            var caught = CaughtExceptionPattern.Match(error);
            if (caught.Success)
            {
                throw new FormulaEvaluationException(_expression,
                    $"Evaluating formula '{_expression}' was aborted by {caught.Groups["type"].Value}: {error}");
            }
        }

        return result;
    }
}
