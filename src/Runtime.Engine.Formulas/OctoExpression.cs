using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// mXparser <see cref="Expression"/> pre-loaded with the OctoMesh formula extensions:
/// the <c>now(addMinutes)</c>, <c>startOfDay(dayCount)</c> and <c>isnull(x)</c> functions and the
/// <c>null</c> constant (mapped to <see cref="double.NegativeInfinity"/>, the engine's null sentinel).
/// </summary>
/// <remarks>
/// NULL operands (AB#8060): a caller binds an operand that has no value as <see cref="double.NaN"/>.
/// NaN propagates through arithmetic, comparisons and functions, so an unhandled NULL makes the result
/// NULL. <c>if(cond, a, b)</c> returns the branch it takes, so a NULL operand in the other branch does
/// not matter (a NULL condition makes the result NULL). <c>coalesce(x, default)</c> (mXparser built-in, first non-NaN argument) and
/// <c>isnull(x)</c> handle a NULL explicitly.
/// </remarks>
public class OctoExpression : Expression
{
    private readonly Function _nowFunction = new("now", new NowFunction());

    private readonly Constant _nullFunction = new("null", double.NegativeInfinity);
    private readonly Function _startOfDayFunction = new("startOfDay", new StartOfDayFunction());
    private readonly Function _isNullFunction = new("isnull", new IsNullFunction());

    /// <summary>
    /// Creates an expression for <paramref name="expressionString"/> with the OctoMesh
    /// functions and constants registered.
    /// </summary>
    public OctoExpression(string expressionString) : base(expressionString)
    {
        addDefinitions(_startOfDayFunction);
        addDefinitions(_nowFunction);
        addDefinitions(_nullFunction);
        addDefinitions(_isNullFunction);
    }
}
